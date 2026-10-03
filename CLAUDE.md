# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Grundlagen

- **Was:** Mod-Manager für Dyson Sphere Program (Youthcat Studio) als KroModIx-Plugin. Steam-AppId **1366540**, Plugin-Id `kroste.dysonsphereprogram`.
- **Stack:** .NET 10, `KroModIx.Plugin.Contracts` als PackageReference, `minHostVersion` 1.27.0.
- **Repo:** `github.com/KroModIx/KroModIx.Plugin.DysonSphereProgram`.
- **Deploy-Ziel:** `~/.config/KroModIx/plugins/kroste.dysonsphereprogram/`.
- **Datenquelle:** Nexus Mods, Game-Slug **`dysonsphereprogram`** (`DspNexusCatalog.GameSlug`), konsumiert über `_host.Nexus` — kein plugin-eigener HTTP-Client.
- **Kroste-Standards:** `~/.claude/skills/KroModIx-Plugin/`. Hier steht nur, was DSP-spezifisch ist.

## Dieses Repo ist die Referenz für zwei Skill-Kernprinzipien

Wer an den entsprechenden Stellen anderer Plugins arbeitet, portiert von hier:

- **`NexusFileNameParser` mit beiden CDN-Formaten** (Kernprinzip 8). Der Bug wurde hier gefunden: `Locale-15-1-0-1703155833.7z` (Dash + Unix-Timestamp) matchte den vorhandenen Parser nicht, `TryExtractModId` gab still `null` zurück, und Downloads/Installiert blieben wochenlang ohne Cover und Details — ohne eine einzige Fehlermeldung. Seit v0.6.1 matchen beide Formate plus `.zip`/`.rar`/`.7z`.
- **Manifest-GC gegen Phantom-Update-Badges** (v0.6.4). `InstalledKeysProvider` filtert verwaiste Install-Manifests vor dem Version-Vergleich. Beim Ändern gilt die Konsistenzfalle aus dem Skill: der Key im GC-Callback muss aus derselben Quelle gebaut werden wie beim Install, sonst läuft der Store beim ersten Check leer.

## Loader und Pfade

DSP lädt Mods über **BepInEx**:

- Mod-Verzeichnis: `<InstallDir>/BepInEx/plugins`
- Loader erkannt an: `<InstallDir>/BepInEx/core/BepInEx.dll`
- Fehlt BepInEx, führt `BepInExBootstrapper` durch die Installation.
- **Backup-Ziel ist `<InstallDir>/BepInEx/` komplett** — nicht nur `plugins/`, weil ein Install auch `core/`, `config/` und `logs/` anfasst.

## Architektur

- **Services/**: `DspPathResolver` / `DspPaths`, `BepInExBootstrapper` + `BepInExScanner` (Loader), `DspNexusCatalog` (GraphQL-Vollkatalog mit Pagination), `DspNexusRowEnricher` (Downloads + Installiert teilen ihn), `NexusFileNameParser`, `DspInstallManifestStore`, `DspInstallService` / `DspZipInstaller` (SharpCompress, Zip-Slip-Guard auch im Ordner-Layout-Pfad seit v0.7.0), `DspDownloader`, `DspUpdateChecker`, `CoverCache`, `DownloadEventBus`.
- **Views/**: Nexus (Katalog), Downloads, Installiert, `NexusModDetailWindow` + VM, `DspNexusDetailLauncher` als geteilter Öffner für alle drei Tabs.

## Bekannte Grenzen

- **Kein Enable/Disable pro Mod** — BepInEx lädt, was in `plugins/` liegt.
- **Kein Dependency-Resolver.** Mods mit Abhängigkeiten brauchen manuellen Zusatz-Install.
- Der Versions-Vergleich kommt seit v0.7.0 aus dem Contracts-Baukasten (`VersionCompare`) — keine plugin-eigene Vergleichslogik mehr nachbauen.

## Archive und GitHub kommen aus dem Host (ab v0.9.0)

`DspZipInstaller` bekommt `IHostServices.Archives`, `BepInExBootstrapper`
zusätzlich `.GitHub`. SharpCompress ist aus dem Plugin verschwunden.

**Die fest hinterlegte Ausweich-URL ist weg.** Sie zeigte auf `v5.4.23.5`
und wäre mit jeder neuen BepInEx-Ausgabe weiter veraltet. Wer beim
GitHub-Limit landete, bekam stillschweigend eine alte Fassung, ohne es zu
erfahren.

**Bei BepInEx mit einer Zusatzschwierigkeit, die MelonLoader nicht hat: der
Dateiname trägt die Version.** `BepInEx_win_x64_5.4.23.5.zip` zum Tag
`v5.4.23.5`. Greift die Raten-Sperre, kennt der Baukasten nur den Tag — aber
`GitHubRelease.Version` liefert die Fassung ohne führendes `v`, und genau die
steht im Dateinamen. Der Umleitungs-Pfad kommt damit ohne API-Aufruf zur
richtigen URL. Als Test festgehalten
(`Bei_Raten_Sperre_entsteht_der_Dateiname_aus_der_Fassung`).

**Der Namensvergleich läuft über Anfang *und* Ende.** Die v6-Vorabausgaben
heißen `BepInEx-Unity.IL2CPP-win-x64-*` — Bindestriche statt Unterstriche.
DSP ist Unity **Mono**; die IL2CPP-Fassung würde nicht laden. Ein „enthält
BepInEx und endet auf .zip" hätte sie genommen.

**Der eigene Ausbruch-Schutz war richtig gerechnet — und meldete trotzdem
Erfolg.** Er löste jeden Pfad gegen das Ziel auf, übersprang den abgelehnten
Eintrag dann aber **still**. Wer ein Archiv mit einem Ausbruchsversuch
installierte, sah „Direkt-Layout: 42 Datei(en)" und erfuhr nichts von der
dreiundvierzigsten. Jetzt bricht der Install ab und nennt die Einträge. Was
dem eigenen Schutz außerdem fehlte: die Laufwerksbuchstaben-Prüfung —
`C:\evil.dll` ist auf Linux nicht „rooted" und landete als Ordner namens
`C:` **innerhalb** des Ziels. Kein Ausbruch, aber plattformabhängig.

**Das Ordner-Layout (einziger Wurzelordner → `BepInEx/plugins/<name>/`)**
läuft jetzt über `StripPrefix`. Ein README **neben** dem Ordner wird davon
von sich aus übersprungen; früher lief genau das in eine
`ArgumentOutOfRangeException` im Substring. Der Wurzel-Ordnername kommt aus
dem Archiv und wird deshalb **vor** dem Anlegen durch denselben Schutz
geschickt.

**Der Bootstrap war ungetestet.** Jetzt 23 Tests, darunter welche URL er
anfragt, der Raten-Sperren-Zweig, die IL2CPP-Abgrenzung, und dass ohne
auffindbare Ausgabe gemeldet statt geraten wird.
