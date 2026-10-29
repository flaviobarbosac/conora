# App / Universal Links (well-known)

## Android (`assetlinks.json`)

Uses the **Google Play App Signing** SHA-256 certificate fingerprint (the cert that signs what users install from the Play Store).

Do **not** put the upload-keystore SHA-256 here for production App Links — Play re-signs the AAB. Upload fingerprint (`5B:2C:19:…`) only matches sideloaded/local builds.

Current Play App Signing SHA-256:
`A9:FC:0A:13:7B:1B:44:15:1C:A0:94:53:94:92:EE:B4:64:65:7B:40:D4:8F:B9:7F:7F:CA:4C:E7:71:4A:BC:1F`

## iOS (`apple-app-site-association`)

Uses Apple Team ID + bundle id: `N2JF8644LJ.br.com.conora.app`.
Universal Links do not use the upload/distribution cert fingerprint in this file.
