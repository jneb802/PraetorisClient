# Road travel wagon bundle

`Assets/Bundles/praetorisclient` is an asset bundle embedded in `PraetorisClient.dll` as
`PraetorisClient.Bundles.praetorisclient`. It contains one prefab, `PraetorisRoadStop`: the road travel wagon.

## Source

The prefab is authored in the Valheim rip project, not in this repository:

- Project: `~/Develop/valheimRipCombatArms/valheimRipCombat` (Unity 6000.0.46f1)
- Prefab: `Assets/WarpProjects/Praetoris/RoadTravel/PraetorisRoadStop.prefab`, bundle name `praetorisclient`
- Mock placeholders: `Assets/WarpProjects/Praetoris/RoadTravel/Mocks/`

The prefab holds no vanilla content. Jotunn replaces these mocks when the piece registers:

| Mock | Resolves to |
|---|---|
| Child `Wagon/JVLmock_trader_wagon_destructable__default` | The `default` model child of the vanilla `trader_wagon_destructable` prefab (mesh, materials, collider) |
| Child `Guide/JVLmock_Haldor__HaldorTheTrader` | The model child of the vanilla `Haldor` prefab (no `Trader` or `ZNetView`) |
| Sprite `JVLmock_mapicon_trader` | The vanilla trader map icon, used as the build icon |
| Effect prefabs `JVLmock_vfx_SawDust`, `JVLmock_sfx_wood_hit`, `JVLmock_sfx_wood_destroyed`, `JVLmock_vfx_wagon_destroyed`, `JVLmock_vfx_Place_wood_wall`, `JVLmock_sfx_build_hammer_wood` | The vanilla effects of the same names |

The root has `ZNetView` (persistent), `Piece` (`m_primaryTarget` so monsters attack it) and `WearNTear` (2,500 health,
wood, no support or roof wear). Local +Z faces the road. The wagon sits 3 m behind the guide, turned to run parallel to
the road. PraetorisClient adds its travel component to `Guide`, so only the guide is interactable, and moves the vanilla
wagon collider to the `piece` layer after the mocks resolve.

## Rebuild

Always build the single bundle, never All Bundles:

1. In the AssetBundle Browser (Build tab), select the `praetorisclient` bundle. Keep StandaloneWindows, Standard
   Compression and `AssetBundles/StandaloneWindows`.
2. Build, then check `AssetBundles/StandaloneWindows/praetorisclient.manifest`: one asset
   (`PraetorisRoadStop.prefab`) and no dependencies.
3. Copy `AssetBundles/StandaloneWindows/praetorisclient` to `Assets/Bundles/praetorisclient` here and rebuild the mod.
