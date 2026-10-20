# Privacy Policy for StripWolf

**Effective Date: June 14, 2026** · **Last updated: October 20, 2026**

This privacy policy governs your use of the software application **StripWolf** ("Application") for mobile and desktop devices. StripWolf is a comic book reader that supports local files and self-hosted server integrations (such as Komga).

## 1. Information Collection and Use
StripWolf is designed with a "local-first" privacy model. 

* **No Personal Data Collection**: We do not collect, harvest, or transmit any personally identifiable information (PII) or usage data to our servers. We do not run any servers for the Application.
* **Local Data Storage**: All library data, reading progress, login credentials for self-hosted servers, and personal reading statistics are stored strictly on your local device. 
* **Self-Hosted Integrations**: If you connect the Application to a self-hosted server (e.g., Komga), your server address and user name are stored locally in the settings. Passwords and API keys are stored in a separate, encrypted file. On Windows the encryption key is protected by Windows for your user account; on other systems the key and the credentials file can only be read by your user account (Android and iOS keep them in the Application's private storage). Connections are made directly between your device and your server; we have no access to this traffic.
* **Cloud Folders**: If you add a cloud or other storage folder, the Application stores a reference to that folder locally so it can import the comics in it again. The files are read through your device's storage provider; we have no access to them.
* **Update Check**: About once a week (and when you check manually) the Application asks GitHub for the latest StripWolf release (`api.github.com/repos/dapplo/StripWolf/releases/latest`). The request contains no personal data, but like any internet request it reveals your IP address to GitHub, whose [privacy statement](https://docs.github.com/site-policy/privacy-policies/github-general-privacy-statement) applies.

## 2. In-App Purchases & Payments
The Application offers an optional premium unlock. 
* Payment transactions are processed entirely and securely by Google Play Billing (Google Play Store). 
* We do not collect, process, or store your payment details (such as credit card numbers or billing addresses).

## 3. Data Backups
If you enable Android Auto Backup on your device, a copy of the Application's settings (server addresses, user names and preferences) may be backed up to your personal Google Drive account. Passwords and API keys are not included in the backup, so after restoring to a new device you need to enter them again. This backup data is encrypted and subject to Google's standard backup terms and privacy policies.

## 4. Crash Logs & Issue Reporting
If you choose to submit a bug report via the "Report an Issue" button, the Application pre-populates a template containing:
* Your Operating System type and version (e.g. Android 13, Windows 10)
* The version of StripWolf you are running
No personal files, browsing history, or private data are attached. You will have full visibility of the report text before submitting it to our public GitHub issue tracker.

## 5. Contact Us
If you have any questions regarding privacy while using the Application, please contact us via the GitHub project repository:
https://github.com/dapplo/StripWolf
