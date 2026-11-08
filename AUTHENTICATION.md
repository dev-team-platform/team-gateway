# Gateway authentication configuration

The gateway owns the browser session and is the only service that needs the RSA private key. Each backend receives only the public key.

Generate a 3072-bit development key pair outside source control:

```bash
openssl genpkey -algorithm RSA -pkeyopt rsa_keygen_bits:3072 -out gateway-internal-jwt-private.pem
openssl rsa -pubout -in gateway-internal-jwt-private.pem -out gateway-internal-jwt-public.pem
```

Configure `InternalJwt:PrivateKeyPem` for `TeamGateway.Api` and the matching `InternalJwt:PublicKeyPem` for `TeamNotificationService.Api`. Environment variable names use double underscores: `InternalJwt__PrivateKeyPem` and `InternalJwt__PublicKeyPem`. Keep the private key in a secret store; never give it to an application backend.

The notification service must be reachable only through the gateway in deployed environments. It validates issuer `team-gateway`, audience `team-notification-service`, signature, and expiry, so Keycloak access tokens and browser cookies are not accepted by it.

Frontend flow:

1. Navigate to `GET /api/v1/auth/login?returnUrl=/...` when unauthenticated.
2. After the callback returns to the frontend, call authenticated `GET /api/v1/auth/csrf`; read the `XSRF-TOKEN` cookie and send it as `X-XSRF-TOKEN` on every unsafe gateway request, including SignalR negotiate.
3. Connect SignalR to the gateway path `/hubs/notifications`; do not supply a Keycloak token. The browser cookie authenticates the gateway, which adds the internal bearer token upstream.
