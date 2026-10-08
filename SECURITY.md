# Security Policy

## Your responsibility as the installer

Salah Times is an unofficial, community project provided **"as is", without warranty of any kind** (see [LICENSE](LICENSE)). It is not distributed through the Microsoft Store and there are no prebuilt packages: you build it from source and install it yourself. **You are responsible for the security of your own system.** In particular:

- **Review the code and scripts** before you run them. The install scripts change system settings.
- **Developer Mode** (quick install) lowers some of Windows' app-installation protections. Turn it off again if you no longer need it.
- **Trusting a certificate** (signed install) means Windows will accept *any* package signed with it.
  - Only trust a certificate you created yourself, with `scripts\New-DevCert.ps1` on your own machine.
  - Never import a certificate or `.pfx` file that someone else sends you.
  - Remove the certificate with `scripts\Uninstall-Package.ps1 -RemoveCertificate`, run elevated, when you uninstall.
- **Never share** your signing certificate's private key, and never commit it to a repository.

The maintainers accept no liability for any damage, data loss or security issue that results from installing or using this software.

## Supported versions

Only the latest version on the `main` branch receives fixes.

## Reporting a vulnerability

If you find a security problem in the code or the install scripts, **please don't open a public issue.** Report it privately through GitHub instead: **Security** tab → **Report a vulnerability**.

Please include:
- a description of the problem and its impact
- steps to reproduce it
- the commit you tested

This is a volunteer project, so there is no guaranteed response time. Reports will be acknowledged when possible, and fixes released as time allows. Please give a reasonable amount of time to fix the issue before disclosing it publicly.
