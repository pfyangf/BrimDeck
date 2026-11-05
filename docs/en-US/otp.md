# Authenticator

BrimDeck can show one-time passwords (TOTP) in settings, for sites that use two-factor authentication.

Open **Settings › Verification › Authenticator**. Settings opens from the gear at the top right of the expanded panel, from a right-click on the panel, or from the tray icon.

## View

Each account is one row: issuer, account name, the current 6-digit code, and how many seconds are left in this step. The code changes about every 30 seconds, as in a normal authenticator. The bar changes to a warning color when fewer than 5 seconds remain.

The default is HMAC-SHA1, 30 seconds and 6 digits (RFC 6238).

## Add

Either way works. Typing the secret does not need a QR code:

1. **Type it.** Enter an optional issuer and account name, and the Base32 secret (spaces are fine), then select Add.
2. **QR code.** Select Import from image and choose a PNG, JPEG, BMP or GIF that contains `otpauth://totp/...`. Or copy the QR image to the clipboard and select Paste from clipboard.

If the QR code asks for SHA-256, SHA-512, 6 to 8 digits, or a period from 15 to 120 seconds, that is what is saved. HOTP (a counter code) is not supported.

## Delete

Delete on a row asks first. After you confirm, the account is removed from this PC and cannot be undone.

## Where the secret is stored

Secrets are written to `%LOCALAPPDATA%\BrimDeck\otp.dat`. The file is one encrypted blob of the account list, using Windows DPAPI for the current user (`ProtectedData`, `CurrentUser`), the same protection as the API-key file `secrets.dat`. It is not plaintext JSON, and other Windows users cannot decrypt it.

The issuer and account name are inside that blob so the list can show them. Codes are not uploaded and are not written to `settings.json`. Moving to another PC, or deleting this Windows user's data, means adding the secrets again.
