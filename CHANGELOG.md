# Changelog

## [1.0.2] - 2026-03-18

### Fixed
- Infinite loop in SerializedObject property traversal for assets with circular managed references ([SerializeReference])

### Changed
- Scan constants (MaxPropertyIterations, BatchTimeMs, DefaultItemsPerFrame) centralized in Settings

## [1.0.1] - 2026-03-16

### Fixed
- Batched project scanning to prevent editor freezing on large projects
- Error handling during asset scanning with logging

### Changed
- Scanning logic extracted to dedicated ProjectScanner class

## [1.0.0] - 2026-03-06

### Added
- Two-way reference tracking (Used By / Uses) for project assets and scene objects
- Reference count overlay in the Project window (configurable left/right position)
- Binary cache with incremental updates via AssetPostprocessor
- YAML scanning (line-by-line guid extraction) and binary scanning (SerializedObject traversal)
- Folder, TerrainData, and Prefab/GameObject scanning
- Scene object support (GO-to-GO and GO-to-asset references, PrefabStage aware)
- Multi-selection support with merged results
- Virtualized scroll for large result lists
- Configurable Settings ScriptableObject (scan scope, ignore rules, display, performance)
- Auto-migration from JSON to binary cache format
