# Utilitaire evenements Facebook

L'executable principal est disponible ici :

```text
Gestion evenements Facebook.exe
```

La version publiee par .NET se trouve aussi ici :

```text
tools/dist/PulsationEventManager/PulsationEventManager.exe
```

## Flux recommande

1. Ouvre `Gestion evenements Facebook.exe`.
2. Colle l'URL de l'evenement Facebook dans le champ `URL Facebook`.
3. Clique `Importer depuis Facebook`.
4. Verifie les champs recuperes : titre, date, heure, lieu, danse, type et image.
5. Si tout est bon, l'outil enregistre l'evenement.
6. Si `Publier automatiquement` est coche, l'outil fait aussi le commit et le push.

Si Facebook ne donne pas assez d'information, l'outil affiche un message de fallback. Dans ce cas, complete les champs a la main comme avant, puis clique `Enregistrer`.

## Ce que l'outil met a jour

L'utilitaire garde ces fichiers synchronises :

```text
data/evenements.json
data/evenements.js
ou-danser.html
```

Si une image est recuperee ou importee, elle est placee dans :

```text
assets/images/events/
```

`ou-danser.html` est mis a jour avec les donnees structurees `Event`, ce qui aide Google a comprendre les evenements, leurs dates et leurs lieux.

## Publication automatique

Le bouton `Commit + push` publie seulement les fichiers generes par l'outil :

```text
data/evenements.json
data/evenements.js
ou-danser.html
assets/images/events/...
```

Le message de commit utilise est :

```text
Mettre a jour les evenements Facebook
```

Si Git refuse le commit ou le push, l'evenement reste quand meme enregistre localement. Il faut alors publier manuellement ou corriger Git.

## Import Facebook

L'import automatique essaie deux methodes :

- Graph API, si un token est configure.
- Lecture publique de la page Facebook, si aucun token n'est disponible ou si l'API refuse l'evenement.
- Navigateur integre WebView2, si Facebook bloque la lecture publique.

Si le navigateur integre s'ouvre, connecte-toi a Facebook au besoin, attends que la page de l'evenement soit visible, puis clique `Utiliser cette page`.

Facebook peut masquer certaines donnees ou bloquer la lecture publique. C'est normal : le fallback manuel reste disponible pour cette raison.

## Token optionnel

Pour rendre l'import plus fiable, tu peux configurer un token Meta/Facebook dans une variable d'environnement Windows :

```text
PULSATION_FACEBOOK_ACCESS_TOKEN
```

L'outil accepte aussi ces noms :

```text
META_FACEBOOK_ACCESS_TOKEN
META_PAGE_ACCESS_TOKEN
```

La version Graph API utilisee par defaut est configurable avec :

```text
PULSATION_FACEBOOK_GRAPH_VERSION
```

Exemple de valeur :

```text
v25.0
```

Ne mets jamais le token directement dans le code ou dans Git.

## Champs obligatoires

Pour qu'un evenement soit considere complet, il faut au minimum :

- un titre;
- une date de debut;
- au moins un style de danse.

Si une de ces informations manque, l'outil te demande de completer le formulaire avant de publier.
