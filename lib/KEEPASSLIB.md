# Vendored KeePassLib

- Source: KeePass 2.61.1 source package, https://sourceforge.net/projects/keepass/files/KeePass%202.x/2.61.1/KeePass-2.61.1-Source.zip/download
- Package SHA-256: 711BD9EC7076661678469E51BAE876DEBAC07A08C32F1B1C6B3B8FE5E761C13A (matches https://keepass.info/integrity.html)
- Folder `KeePassLib/` is byte-identical to the package's `KeePassLib/` folder. Do not edit it.
- Built by `KeePassLib.csproj` (ours). The official `.csproj`/`.pfx` files inside the folder are kept unmodified and unused.
- License: GPL-2.0-or-later (see repository `LICENSE`).
- To update: replace the folder with a newer official release, verify its hash against keepass.info, regenerate `keepasslib.sha256`.
# Vendored KeePass data

- `KeePassData/MostPopularPasswords.txt.gz`: byte-identical to `KeePass/Resources/Data/MostPopularPasswords.txt.gz` in the same 2.61.1 source package (SHA-256 54283F9614F5F6879891B0DB5FD16300289FEC45DF8C08108E36B56734E3682F, 25,584 bytes; listed in `keepassdata.sha256`).
- Purpose: KeePass loads it at startup via `PopularPasswords.Add(bytes, true)`; without it the strength estimator skips the common-password check. Embedded as a resource in LockPad.Core.

