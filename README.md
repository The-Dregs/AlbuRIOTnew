### AlbuRIOT

AlbuRIOT is a multiplayer survival game that brings Philippine mythology to life through dynamic, replayable gameplay. Players take on the role of an albularyo (folk healer) who must purify an island inhabited by mythological beings. Core systems include a power-stealing mechanic, Perlin noise-based procedural map generation, and Behavior Tree-driven enemy AI.

### Key Features
- **Philippine mythology focus**: Creatures like the Manananggal, Tikbalang, and Aswang are central to gameplay and lore.
- **Power-stealing mechanic**: Defeat creatures to temporarily acquire their abilities for strategic depth.
- **Procedural worlds**: Perlin noise-driven terrain and resource placement ensure varied sessions and high replayability.
- **Adaptive enemy AI**: Behavior Tree AI supports reactive, coordinated enemy behaviors.
- **Multiplayer**: Cooperative and competitive modes with synchronization and stability as core goals.

### Objectives (from research brief)
- Integrate Philippine folklore respectfully into core gameplay, visuals, and audio.
- Implement Perlin noise-based terrain, points of interest, and resource distribution balanced for fairness and performance.
- Design Behavior Tree AI for intelligent enemy decision-making and group coordination.
- Deliver co-op and competitive modes with stable networking and responsive play.
- Conduct user testing to refine balance, progression, UI/UX, and cultural sincerity.

### Non-Functional Goals
- **Performance**: Target stable FPS, efficient load times, and sensible resource use.
- **Usability**: Accessible UI, clear onboarding, and responsive controls.
- **Security**: Protect player data, especially in online play.
- **Compatibility**: Consistent experience across target platforms.

### Gameplay Modes
- **Cooperative survival**: Team up to survive and cleanse the island.
- **Competitive**: Battle other players while contending with mythological threats.

### Tech Overview
- **Engine**: Unity
- **Language**: C#
- **Procedural generation**: Perlin noise for terrain, POIs, and resource distribution
- **AI**: Behavior Trees for adaptive enemies

### Open in Unity
The Unity project is located in `ALBURIOT/`.
- Open Unity Hub, click "Add", and select the `ALBURIOT/` directory.
- Let Unity regenerate `Library/`, `obj/`, and other cache folders as needed.

### Project Structure (high-level)
- `ALBURIOT/Assets/` – Game code, scenes, art, audio, and settings
- `ALBURIOT/ProjectSettings/` – Unity project settings
- `ALBURIOT/Packages/` – Unity package manifest and lock file
- `docs/` – Project documentation (`ALBURIOT.TXT`, `STORYLINE.TXT`)

### Roadmap & Deliverables (abridged)
- Prototype with co-op and competitive modes
- Perlin noise-based procedural generation
- Behavior Tree AI for adaptive enemies
- Power-stealing system with tuning for balance
- Culturally inspired visual/audio assets
- User testing reports and technical documentation

### Storyline
`STORYLINE.TXT` is currently a placeholder in `docs/`. As narrative content is finalized, key beats will be summarized here.

### Credits
Proponents: Rod Anthony Balaoro, Sebastian Baluyut, Ricardo Jose Colarina, Rhycell Ortega

### License
TBD
