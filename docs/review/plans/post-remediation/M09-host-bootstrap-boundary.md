# M09 — Вынести host file I/O из Core ContentBootstrap

**Source review:** [`CODE_REVIEW_POST_REMEDIATION_2026-08-11.md`](../../CODE_REVIEW_POST_REMEDIATION_2026-08-11.md) § M9  
**Severity:** Medium · **Домен:** architecture boundaries · **Roadmap:** P2  
**Related residual:** R25 (partial)  
**Статус валидации:** ✅ Подтверждено по коду

---

## Проблема

`SteelConveyorWar.Core.Hosting.ContentBootstrap` выполняет path resolution и `File`/`Directory` I/O. Дублирование Client/Headless убрано, но нарушено правило «Core parse; hosts I/O».

---

## Доказательства

- Documented rule «Core parse; hosts I/O»: `docs/MVP_IMPLEMENTATION_DECISIONS.md:29-31`
- Implementation FS I/O in Core: `ContentBootstrap.cs:15-37` (`Directory.Exists`), `:44-95` (`File.ReadAllText`)
- Callers: `Client/Program.cs:9`, `Headless/Program.cs:7`
- R25 plan explicitly allowed Core hosting helper vs separate Hosting project and chose Core — это и есть boundary regression
- `ContentBootstrapTests` — disk Load; нет proof «Core without filesystem»

---

## Целевой дизайн

**Option A — new project `SteelConveyorWar.Hosting`:**  
References Core; owns ResolveConfigDirectory + Load files; returns catalogs/`GameCreationOptions`. Client/Headless reference Hosting.

**Option B — keep parse API in Core, move I/O to shared host file in both projects:**  
Worse duplication risk — prefer A.

**Option C — Core accepts `IContentFileSystem` abstraction:**  
Still pulls I/O policy into Core; weaker than A.

**Recommendation:** Option A.

Core keeps: `*Loader.Parse(string json)`, validators, `GameCreationOptions`.

---

## План фикса

1. Create `src/SteelConveyorWar.Hosting` class library (no SFML).
2. Move `ContentBootstrap` + `LoadedGameContent` there.
3. Update Client/Headless/csproj/sln references.
4. Keep/move tests: `ContentBootstrapTests` → Hosting.Tests or Core.Tests with parse-only + Hosting.Tests for I/O.
5. Update AGENTS.md / decisions docs.
6. Ensure Core.Tests still don't need filesystem for unit parse tests.

---

## Definition of Done

- [ ] Core has no `File.ReadAllText` / config path probing for game content.
- [ ] Single shared host bootstrap remains (no Client/Headless drift).
- [ ] R25 marked closed without boundary regression.
- [ ] verify green.

## Специалисты

`engine-architect`, `test-ci`.
