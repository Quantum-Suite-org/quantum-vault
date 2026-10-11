# Quantum Vault — Privacy Policy

Effective date: 11-10-2026
Applies to: Quantum Vault 0.1.2 and later

## Summary

Quantum Vault stores your notes, snippets and commands on your own computer and does not send them anywhere automatically. It does not send analytics, telemetry or crash reports. It does make network requests in a small number of situations, all of which are listed in the "Network Activity" section below. Those requests happen only when you use the feature that needs them.

## What Quantum Vault Stores Locally

Quantum Vault stores only what you explicitly put into it:

- Your notes, snippets and commands, encrypted on your local machine
- Vault configuration and metadata
- Log files that contain operation metadata only (see "Logs")
- The optional language model, if you choose to download it (see "Assistant Model")

All local data is stored in: `%APPDATA%\Quantum-suite-org\QuantumVault\`

## Network Activity

Quantum Vault makes network requests only in the following cases.

1. **Assistant model download.** The first time you use the assistant, or when you choose to download the model, the app downloads the language model files from the Hugging Face model repository. The request reveals your IP address and the fact that a download occurred, to Hugging Face, in accordance with Hugging Face's own privacy policy.

2. **Assistant web tools.** When the assistant uses its web search or page-fetch tools to answer a question, the search query or the page address it has chosen is sent to the search or web service involved. The contents of your vault are not sent unless the assistant includes them in a query you have asked it to make. Those services handle your request under their own privacy policies. Avoid entering confidential information into assistant prompts when web tools may be used.

3. **Windows, WebView2 and winget.** Windows and the Microsoft Edge WebView2 Runtime may make network requests as part of their normal operation, and winget contacts its package source when you install or update the app. Those requests are governed by Microsoft's policies and are outside the control of Quantum Vault.

Quantum Vault does not contact any server operated by its publisher. Quantum Vault does not send your notes, snippets, commands, master password, assistant conversation history, or any identifier to its publisher.

## What Quantum Vault Does NOT Collect

- No telemetry
- No crash reports sent anywhere
- No usage analytics
- No account information
- No advertising identifiers
- No third-party analytics or tracking SDKs

## Encryption

- **Notes** are encrypted with AES-256-CBC. The key is derived from your master password using PBKDF2-SHA256 with 200,000 iterations. The derived key is not written to disk.
- **Snippets and commands** are encrypted with AES-256-CBC using a randomly generated key stored in `vault.key` in your data directory. Anyone with access to that file and to `vault.json` can read these entries, so protect the data directory accordingly.
- **DPAPI-protected notes** are additionally bound to your Windows user account using the Windows Data Protection API. They cannot be decrypted on another machine or by another Windows user account.

Cryptography is provided by the Windows Cryptography Next Generation (CNG) interface and the Windows Data Protection API. No encryption is perfect, and the publisher does not guarantee the security of your data. See the End User License Agreement for the full terms.

## Assistant Model

The assistant runs its language model locally on your computer. Your prompts and the model's replies are not sent to the publisher. The model is subject to its own licence terms, which are published by its authors.

## Logs

Quantum Vault writes local log files at:

- `%APPDATA%\Quantum-suite-org\QuantumVault\logs\qvault.log`
- `%APPDATA%\Quantum-suite-org\QuantumVault\logs\qvault_gui.log`

Logs contain timestamps of operations (for example initialisation, shutdown, save and load), entry counts and error codes. Logs are not sent anywhere.

Logs do not contain note, snippet or command content, passwords, key material, or personal information.

## Exports and Backups

When you export your vault (Settings → Export Vault, or `qvault export`), the resulting JSON file contains plaintext content. The file is written only to the location you choose. Keep it secure, because anyone who can read it can read the content.

Backups created by `qvault backup` are copies of the encrypted `vault.json` file. They remain protected by your master password.

## Third Parties

Quantum Vault does not embed analytics or advertising SDKs. Third parties involved in its operation are limited to the services described under "Network Activity" and the software components described in the End User License Agreement.

## Children

Quantum Vault is not directed at children. Under India's Digital Personal Data Protection Act, 2023, a child (a person under 18) requires verifiable parental consent for certain processing of personal data. The publisher does not knowingly collect personal data from children.

## Your Control

- You decide what to store in the vault and what to delete.
- You can uninstall the application. Uninstalling does not delete your data directory. You can delete that directory yourself if you want to remove all local data.
- You can avoid all network activity by not downloading the assistant model and by not using the assistant's web tools.

## Data Protection (India)

If you are located in India, you may have rights under the Digital Personal Data Protection Act, 2023 in relation to personal data processed by others. Quantum Vault does not collect personal data from you for its publisher. Your use of the app to store personal data of other people is governed by the End User License Agreement, Section 4.2.

## Changes to This Policy

This policy may be updated. The effective date at the top of the document shows the latest version. Material changes will be noted in the release notes of the version that introduces them.

## Contact

Questions about this policy may be raised through the public issue tracker of the Quantum Suite repository for Quantum Vault.
