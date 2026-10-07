# Super Admin lost-factor recovery

This is a platform-owner-only trusted-server operation. It has no HTTP endpoint. Only a platform owner with approved access to the restricted operations host may run it. Before execution, verify the locked-out Super Admin through the established out-of-band identity check and record that verification in the trusted operations channel. Do not use this operation for a Creator identity or a Super Admin identity that has a Creator `Account`.

The operator must also be represented by an existing Super Admin Identity user with no Creator `Account`. Obtain that user's Identity GUID through the authenticated operations/admin identity inventory and verify the GUID-to-operator mapping in the trusted operations channel before placing it in `--operator-id`. The recovery runner validates the persisted claim and no-Creator invariant for both operator and target. The remaining trust boundary is platform-owner-controlled access to the host, its process environment, and the production database secret; the CLI itself cannot prove who is at the keyboard.

Run from the repository root on a restricted server/operations host with the production `Database__ConnectionString` supplied through the approved secret manager or process environment. Never put database credentials, passwords, TOTP secrets, recovery codes, or other secrets in command arguments. The command accepts only identifiers and explicit acknowledgments:

```powershell
dotnet run --project tools/Davetiye.AdminBootstrap -- `
  --recover-lost-mfa `
  --target-user-id <super-admin-identity-guid> `
  --operator-id <verified-super-admin-operator-identity-guid> `
  --out-of-band-identity-verified `
  --confirm
```

The command refuses to mutate anything unless both `--confirm` and `--out-of-band-identity-verified` are present. It verifies the operator and target's persisted Super Admin claims and absence of Creator Accounts, disables target MFA, replaces the lost authenticator key without displaying it, clears recovery codes, rotates the security stamp, and appends a minimized `SuperAdminMfaLostFactorRecovered` audit record with operator and target IDs. The Identity and audit changes commit in one database transaction. Refused operator/target identities create no audit row. After success, verify the event in the authenticated Admin audit view or query `admin_audit_records` by `event_type = 'SuperAdminMfaLostFactorRecovered'` and the target `subject_id`; check that `actor_id` matches the independently verified operator GUID. Never query or copy authenticator/recovery-code values into the audit channel.

Privileged Super Admin cookies compare the security stamp on every request, regardless of the general cookie stamp interval. Existing sessions are rejected on their next request. The recovered user signs in with their password, receives the `mfa-setup-required-super-admin` session state, and must complete `/api/v1/admin/mfa/enroll` followed by `/api/v1/admin/mfa/verify` before using MFA-complete Admin routes. Verification returns the new recovery codes once; the user must store them securely.
