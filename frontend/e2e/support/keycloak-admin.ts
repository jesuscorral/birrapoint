// Test-only helper around Keycloak's Admin REST API, used to provision/clean up a JUDGE user for
// us1-auth.spec.ts. Deliberately NOT editing infra/keycloak/birrapoint-realm.json: Keycloak only
// imports that file once at container start, so seeding a fixed judge credential there would make
// the first test run pass and every rerun after it flaky (the temp password would already be
// consumed/changed). Values below mirror frontend/src/environments/environment.ts and the
// `birrapoint-api-admin` service-account client in infra/keycloak/birrapoint-realm.json — the
// client secret is the same committed LOCAL-DEV placeholder already used by Program.cs/AppHost.cs
// (FR-046; production injects a real one and never runs this suite against it).

const KEYCLOAK_URL = 'http://localhost:8081';
const REALM = 'birrapoint';
const ADMIN_CLIENT_ID = 'birrapoint-api-admin';
const ADMIN_CLIENT_SECRET = 'dev-only-secret-change-me';

export interface ProvisionedJudge {
  id: string;
  email: string;
  tempPassword: string;
}

interface KeycloakRole {
  id: string;
  name: string;
}

async function getAdminToken(): Promise<string> {
  const response = await fetch(`${KEYCLOAK_URL}/realms/${REALM}/protocol/openid-connect/token`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/x-www-form-urlencoded' },
    body: new URLSearchParams({
      grant_type: 'client_credentials',
      client_id: ADMIN_CLIENT_ID,
      client_secret: ADMIN_CLIENT_SECRET,
    }),
  });
  if (!response.ok) {
    throw new Error(
      `Keycloak admin token request failed: ${response.status} ${await response.text()}`,
    );
  }
  const body = (await response.json()) as { access_token: string };
  return body.access_token;
}

// Mirrors the shape T040's KeycloakAdminClient will use in-app: temporary password + required
// action UPDATE_PASSWORD, so the forced-change flow this spec asserts is the real one (FR-003).
export async function createJudgeUser(email: string): Promise<ProvisionedJudge> {
  const token = await getAdminToken();
  const tempPassword = `Temp-${crypto.randomUUID()}`;
  const authHeader = { Authorization: `Bearer ${token}` };

  const createResponse = await fetch(`${KEYCLOAK_URL}/admin/realms/${REALM}/users`, {
    method: 'POST',
    headers: { ...authHeader, 'Content-Type': 'application/json' },
    body: JSON.stringify({
      username: email,
      email,
      // firstName/lastName avoid Keycloak's default User Profile config adding an unplanned
      // VERIFY_PROFILE required action after UPDATE_PASSWORD — a real invited judge (T040) would
      // always have these, so this keeps the test flow representative rather than a workaround.
      firstName: 'Test',
      lastName: 'Judge',
      enabled: true,
      emailVerified: true,
      requiredActions: ['UPDATE_PASSWORD'],
      credentials: [{ type: 'password', value: tempPassword, temporary: true }],
    }),
  });
  if (!createResponse.ok) {
    throw new Error(
      `Keycloak user creation failed: ${createResponse.status} ${await createResponse.text()}`,
    );
  }
  const location = createResponse.headers.get('Location');
  if (!location) {
    throw new Error('Keycloak user creation response is missing the Location header');
  }
  const id = location.substring(location.lastIndexOf('/') + 1);

  // Looked up via the user-scoped "available roles" endpoint rather than the realm-scoped
  // /admin/realms/{realm}/roles/{name}: the latter requires the realm-management `view-realm`
  // permission, which the birrapoint-api-admin service account (manage-users/view-users only,
  // per infra/keycloak/birrapoint-realm.json) doesn't have.
  const availableRolesResponse = await fetch(
    `${KEYCLOAK_URL}/admin/realms/${REALM}/users/${id}/role-mappings/realm/available`,
    { headers: authHeader },
  );
  if (!availableRolesResponse.ok) {
    throw new Error(
      `Keycloak available-roles lookup failed: ${availableRolesResponse.status} ${await availableRolesResponse.text()}`,
    );
  }
  const availableRoles = (await availableRolesResponse.json()) as KeycloakRole[];
  const judgeRole = availableRoles.find((role) => role.name === 'JUDGE');
  if (!judgeRole) {
    throw new Error('JUDGE realm role not found among roles available to the new user');
  }

  const assignResponse = await fetch(
    `${KEYCLOAK_URL}/admin/realms/${REALM}/users/${id}/role-mappings/realm`,
    {
      method: 'POST',
      headers: { ...authHeader, 'Content-Type': 'application/json' },
      body: JSON.stringify([judgeRole]),
    },
  );
  if (!assignResponse.ok) {
    throw new Error(
      `Keycloak JUDGE role assignment failed: ${assignResponse.status} ${await assignResponse.text()}`,
    );
  }

  return { id, email, tempPassword };
}

export async function deleteUser(id: string): Promise<void> {
  const token = await getAdminToken();
  const response = await fetch(`${KEYCLOAK_URL}/admin/realms/${REALM}/users/${id}`, {
    method: 'DELETE',
    headers: { Authorization: `Bearer ${token}` },
  });
  if (!response.ok && response.status !== 404) {
    throw new Error(`Keycloak user deletion failed: ${response.status} ${await response.text()}`);
  }
}
