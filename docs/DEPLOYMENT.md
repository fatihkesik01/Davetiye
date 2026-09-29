# VPS / Deployment

This VPS is **shared** with another, unrelated production project called **Lora** (Lora Coffee Company). Lora belongs to a different client and must never be stopped, rebuilt, or have its containers/images/volumes/networks removed or modified, regardless of what task you're doing here.

## How to connect

```
ssh -p 22222 -i "C:\Users\fatih\.ssh\trackme_deploy" deploy@187.77.92.30
```

- Host: `187.77.92.30` (hostname `srv1405059`)
- Port: `22222` (port 22 also listens, but 22222 is the one actually used)
- User: `deploy`
- Key: `C:\Users\fatih\.ssh\trackme_deploy` — local file on the operator's machine only. Despite the "trackme" name, this is the general deploy key for this VPS, not specific to any one project. **Never commit this key file, its contents, or any `.env`/secret values to this repo.**
- `deploy` has passwordless `sudo -n` for at least: nginx reload/config edits, file ops under root-owned dirs like `/opt`.

## What's on this VPS

- **Lora** — `docker compose` project `lora`, containers `lora-api-prod` (127.0.0.1:5051→8080), `lora-frontend-prod` (127.0.0.1:8081→80), `lora-postgres-prod` (127.0.0.1:15433→5432). Volume `lora_postgres_data`, network `lora_lora-internal`. Domain `loracoffeecompany.com.tr`, nginx site `/etc/nginx/sites-available/loracoffeecompany.com.tr`. **Do not touch.**
- **Davetiye** — as of 2026-09-28, nothing is deployed. `/opt/davetiye` does not exist. Project was fully reset (see below) to start over. Previously used ports **5052** (API), **8082** (web), **15434** (postgres) — free to reuse.
- **Ports already taken by other projects, never reuse for Davetiye**: 5051, 8081, 15433 (Lora), 80/443 (nginx), 22/22222 (ssh).

## Deploying Davetiye

The production domain has not been selected yet. The first deployment
must work over the server IP and must not depend on a domain hardcoded in
application code. Public base URL, allowed origins, cookie domain and
similar host values must come from deployment configuration. When a
domain is purchased, add its dedicated Nginx site and HTTPS
configuration without changing application business logic.

Google web OAuth cannot use a raw, non-localhost IP as an authorized
redirect host. The IP-only deployment must therefore remain usable with
email/password authentication. Google login can be tested with
localhost during development and enabled in production after the domain
and HTTPS callback URL are configured.

Raw-IP HTTP is only for private smoke/health checks. Do not expose real
email/password authentication or authenticated user traffic over plain
HTTP. Real user access requires trusted HTTPS, whether it is configured
for an IP-based acceptance environment or the later production domain.

The GitHub repo is private with no deploy key configured, so code reaches the VPS via `git archive`, not `git clone`:

```
git archive HEAD | ssh -p 22222 -i "C:\Users\fatih\.ssh\trackme_deploy" deploy@187.77.92.30 "mkdir -p /opt/davetiye && tar -x -C /opt/davetiye"
```

Then on the VPS: create `/opt/davetiye/.env` (secrets generated on-box via `openssl rand`, never stored in the repo or transferred from a local machine), and run:

```
cd /opt/davetiye && docker compose -f docker-compose.prod.yml --env-file .env up -d --build
```

## Safely removing/resetting the Davetiye deployment

```bash
cd /opt/davetiye
docker compose -f docker-compose.prod.yml --env-file .env down -v   # stops+removes containers, volumes, network
docker rmi davetiye-api:latest davetiye-web:latest                   # remove built images (leave postgres:16-alpine alone — shared with Lora)
sudo rm -rf /opt/davetiye
```

Do **not** run host-wide cleanup commands such as `docker system prune` or
`docker builder prune` on this shared VPS. Even resources not attached to a
currently running container may belong to Lora. Cleanup must remain explicitly
scoped to Davetiye Compose resources and the two named Davetiye images.

After any destructive VPS operation, always verify Lora is untouched:

```bash
docker ps --format '{{.Names}}\t{{.Status}}\t{{.Ports}}'   # must still show lora-api-prod, lora-frontend-prod, lora-postgres-prod
docker volume ls                                             # must still show lora_postgres_data
docker network ls                                             # must still show lora_lora-internal
```

## Health checks after deploy

```bash
curl -i http://localhost:5052/health/live
curl -i http://localhost:5052/health/ready
docker ps
docker logs --tail 80 davetiye-api-prod
```

## Backups

- Start with a daily PostgreSQL logical backup copied to encrypted
  off-site storage. A backup that remains only on this VPS does not meet
  the off-site requirement.
- Backup credentials and destination secrets must remain outside Git.
- Define configurable retention and monitor the backup job for failures.
- Document and regularly test restoration; a successful backup command
  alone is not sufficient verification.
- Keep payment/invoice and required audit retention separate from the
  invitation-content purge policy.
- Evaluate base backups plus WAL archiving / point-in-time recovery when
  application scale or agreed RPO/RTO requirements justify the added
  operational complexity.
