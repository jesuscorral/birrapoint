# Production realm derivation (T130), applied to birrapoint-realm.json by the Dockerfile `realm`
# stage and by RealmProd.test.sh. The local-dev realm (Aspire/E2E) is left untouched.
#  - strip the seeded `organizer`/`organizer` account (E2E-only); the service-account user stays;
#  - drop the local-dev fallback of the API admin client secret (API_ADMIN_CLIENT_SECRET or nothing);
#  - brute-force detection: temporary lockout (never permanent: it would let anyone lock an
#    organizer out for good), doubling wait up to 15 min after 5 failures;
#  - password policy: 12+ chars, not the username/email. No character-class rules on purpose: the
#    API's temporary judge password is 24 random base64 characters and must always satisfy it;
#  - verifyEmail: self-registration grants ORGANIZER, so the address must be proven (needs SMTP).
.users |= map(select(.username != "organizer"))
| .clients |= map(if .clientId == "birrapoint-api-admin" then .secret = "${API_ADMIN_CLIENT_SECRET}" else . end)
| .bruteForceProtected = true
| .permanentLockout = false
| .failureFactor = 5
| .waitIncrementSeconds = 60
| .maxFailureWaitSeconds = 900
| .minimumQuickLoginWaitSeconds = 60
| .maxDeltaTimeSeconds = 43200
| .passwordPolicy = "length(12) and notUsername and notEmail"
| .verifyEmail = true
