#!/bin/sh
set -eu

# Named Docker volumes are initially root-owned. Prepare only the API's
# persistent Data Protection directory, then run the application unprivileged.
mkdir -p /var/davetiye/dp-keys
chown app:app /var/davetiye/dp-keys

exec setpriv --reuid=app --regid=app --init-groups "$@"
