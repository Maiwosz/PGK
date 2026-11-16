# GtaExtract

Narzędzia (Node.js) tworzące z lokalnie zainstalowanego GTA San Andreas "przepis" na rekonstrukcję
trasy misji *Wrong Side of the Tracks*. Nie kopiują modeli ani tekstur – wyciągają tylko dane geometryczne
(tor, wysokości terenu, pozycje i rozmiary obiektów).

```
node build-recipe.mjs "D:/Gry/Grand Theft Auto San Andreas" "../../Assets/Mission/Recipe"
node render-recipe.mjs "../../Assets/Mission/Recipe"      # podgląd -> recipe-debug.png
```

Potem w Unity mapa generuje się sama (po sklonowaniu projektu albo gdy zmieni się przepis lub kod mapy) jako prefab `Assets/Mission/Generated/Resources/MissionMap.prefab`, a obiekt `MissionMap` w scenie `Assets/Scenes/Game.unity` pokazuje ją w edytorze i w grze. Ręczna regeneracja: `Tools/Mission/Rebuild Map` albo przycisk w inspektorze obiektu `MissionMap`.

Pliki: `img.mjs` (archiwum IMG v2), `gta.mjs` (IDE/IPL/COL), `surfaces.mjs` (typy powierzchni), `png.mjs` (podgląd),
`build-recipe.mjs` (generator przepisu: trasa z `tracks.dat`, siatka wysokości z kolizji, obiekty przy torze).

Układ współrzędnych: GTA (x wschód, y północ, z góra) -> Unity (x, z, y). Kwaterniony IPL w SA są odwrócone
(przed użyciem negujemy x, y, z).
