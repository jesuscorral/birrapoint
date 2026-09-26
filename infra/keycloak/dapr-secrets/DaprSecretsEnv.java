import java.io.IOException;
import java.net.URI;
import java.net.URLEncoder;
import java.net.http.HttpClient;
import java.net.http.HttpRequest;
import java.net.http.HttpResponse;
import java.nio.charset.StandardCharsets;
import java.time.Duration;
import java.util.LinkedHashMap;
import java.util.Map;
import java.util.regex.Pattern;

/**
 * Reads Keycloak's secrets from the Dapr secret store (Key Vault, via the Container App's
 * system-assigned managed identity; T142, ADR-0021) and prints them as shell `export` lines for
 * docker-entrypoint.sh to eval before starting Keycloak. Nothing else is printed to stdout;
 * progress goes to stderr and never includes a secret value.
 *
 * Environment:
 *   DAPR_SECRET_STORE             Dapr secret store component name. Unset: prints nothing (local runs).
 *   DAPR_SECRETS                  Comma-separated ENV_VAR=secret-name pairs.
 *   DAPR_HTTP_PORT                Sidecar HTTP port, injected by Container Apps (default 3500).
 *   DAPR_SECRETS_TIMEOUT_SECONDS  How long to keep retrying (default 300): the sidecar starts next
 *                                 to the app, and a fresh role assignment takes a while to apply.
 *
 * Standard library only (runs on the Keycloak image's JRE; no JSON library there).
 */
public final class DaprSecretsEnv {
    private static final Pattern ENV_NAME = Pattern.compile("[A-Za-z_][A-Za-z0-9_]*");

    public static void main(String[] args) throws Exception {
        var store = System.getenv("DAPR_SECRET_STORE");
        if (store == null || store.isBlank()) {
            return;
        }
        var mappings = parseMappings(orEmpty(System.getenv("DAPR_SECRETS")));
        var port = orDefault(System.getenv("DAPR_HTTP_PORT"), "3500");
        var timeout = Duration.ofSeconds(Long.parseLong(orDefault(System.getenv("DAPR_SECRETS_TIMEOUT_SECONDS"), "300")));
        var deadline = System.nanoTime() + timeout.toNanos();
        var http = HttpClient.newBuilder().connectTimeout(Duration.ofSeconds(5)).build();

        var out = new StringBuilder();
        for (var mapping : mappings.entrySet()) {
            var value = fetch(http, port, store.trim(), mapping.getValue(), deadline);
            out.append("export ").append(mapping.getKey()).append('=').append(shellQuote(value)).append('\n');
        }
        // UTF-8 regardless of the container's locale (the stock image runs with POSIX/C).
        System.out.write(out.toString().getBytes(StandardCharsets.UTF_8));
        System.out.flush();
    }

    static Map<String, String> parseMappings(String spec) {
        var result = new LinkedHashMap<String, String>();
        for (var pair : spec.split(",")) {
            if (pair.isBlank()) {
                continue;
            }
            var eq = pair.indexOf('=');
            if (eq <= 0 || eq == pair.length() - 1) {
                throw new IllegalArgumentException("DAPR_SECRETS entry '" + pair.trim() + "' is not ENV_VAR=secret-name.");
            }
            var env = pair.substring(0, eq).trim();
            var secret = pair.substring(eq + 1).trim();
            if (!ENV_NAME.matcher(env).matches()) {
                throw new IllegalArgumentException("Invalid environment variable name '" + env + "' in DAPR_SECRETS.");
            }
            result.put(env, secret);
        }
        return result;
    }

    private static String fetch(HttpClient http, String port, String store, String secret, long deadline)
            throws InterruptedException {
        var uri = URI.create("http://127.0.0.1:" + port + "/v1.0/secrets/"
                + URLEncoder.encode(store, StandardCharsets.UTF_8) + "/" + URLEncoder.encode(secret, StandardCharsets.UTF_8));
        var request = HttpRequest.newBuilder(uri).timeout(Duration.ofSeconds(10)).GET().build();
        while (true) {
            String problem;
            try {
                var response = http.send(request, HttpResponse.BodyHandlers.ofString(StandardCharsets.UTF_8));
                if (response.statusCode() == 200) {
                    var values = parseFlatJsonObject(response.body());
                    var value = values.containsKey(secret) ? values.get(secret)
                            : values.size() == 1 ? values.values().iterator().next() : null;
                    if (value != null) {
                        return value;
                    }
                    problem = "response did not contain the secret";
                } else {
                    // Status only: the body may echo request details but never needs logging.
                    problem = "HTTP " + response.statusCode();
                }
            } catch (IOException e) {
                problem = "sidecar not reachable (" + e.getClass().getSimpleName() + ")";
            }
            if (System.nanoTime() > deadline) {
                throw new IllegalStateException("Could not read secret '" + secret + "' from Dapr store '" + store + "': " + problem);
            }
            System.err.println("dapr-secrets: waiting for secret '" + secret + "': " + problem);
            Thread.sleep(5_000);
        }
    }

    /** Parses a JSON object whose values are all strings (Dapr's secret response shape). */
    static Map<String, String> parseFlatJsonObject(String json) {
        var parser = new Parser(json);
        var result = new LinkedHashMap<String, String>();
        parser.skipWhitespace();
        parser.expect('{');
        parser.skipWhitespace();
        if (parser.peek() == '}') {
            parser.next();
            return result;
        }
        while (true) {
            parser.skipWhitespace();
            var key = parser.string();
            parser.skipWhitespace();
            parser.expect(':');
            parser.skipWhitespace();
            result.put(key, parser.string());
            parser.skipWhitespace();
            var c = parser.next();
            if (c == '}') {
                return result;
            }
            if (c != ',') {
                throw new IllegalArgumentException("Unexpected '" + c + "' in secret response.");
            }
        }
    }

    /** POSIX single-quoting: the value is taken literally by the shell. */
    static String shellQuote(String value) {
        return "'" + value.replace("'", "'\\''") + "'";
    }

    private static String orEmpty(String value) {
        return value == null ? "" : value;
    }

    private static String orDefault(String value, String fallback) {
        return value == null || value.isBlank() ? fallback : value.trim();
    }

    private static final class Parser {
        private final String text;
        private int position;

        Parser(String text) {
            this.text = text;
        }

        char peek() {
            if (position >= text.length()) {
                throw new IllegalArgumentException("Truncated secret response.");
            }
            return text.charAt(position);
        }

        char next() {
            var c = peek();
            position++;
            return c;
        }

        void expect(char expected) {
            var c = next();
            if (c != expected) {
                throw new IllegalArgumentException("Expected '" + expected + "' in secret response.");
            }
        }

        void skipWhitespace() {
            while (position < text.length() && Character.isWhitespace(text.charAt(position))) {
                position++;
            }
        }

        String string() {
            expect('"');
            var sb = new StringBuilder();
            while (true) {
                var c = next();
                if (c == '"') {
                    return sb.toString();
                }
                if (c != '\\') {
                    sb.append(c);
                    continue;
                }
                var escaped = next();
                switch (escaped) {
                    case '"', '\\', '/' -> sb.append(escaped);
                    case 'b' -> sb.append('\b');
                    case 'f' -> sb.append('\f');
                    case 'n' -> sb.append('\n');
                    case 'r' -> sb.append('\r');
                    case 't' -> sb.append('\t');
                    case 'u' -> {
                        if (position + 4 > text.length()) {
                            throw new IllegalArgumentException("Truncated \\u escape in secret response.");
                        }
                        sb.append((char) Integer.parseInt(text.substring(position, position + 4), 16));
                        position += 4;
                    }
                    default -> throw new IllegalArgumentException("Invalid escape in secret response.");
                }
            }
        }
    }
}
