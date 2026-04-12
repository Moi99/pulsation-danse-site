# Utilitaire événements Facebook

L'exécutable se trouve ici :

```text
tools/dist/PulsationEventManager/PulsationEventManager.exe
```

Une copie pratique est aussi disponible directement à la racine du site :

```text
Gestion evenements Facebook.exe
```

Il sert à gérer le fichier :

```text
data/evenements.json
```

Fonctionnement :

- `Nouveau` vide le formulaire pour créer un événement.
- `Enregistrer` ajoute ou met à jour l'événement sélectionné.
- `Supprimer` retire l'événement sélectionné du fichier JSON.
- `Importer...` copie une image locale dans `assets/images/events/` et inscrit son chemin dans l'événement.
- `Ouvrir Facebook` ouvre l'URL de l'événement dans le navigateur.
- La liste `Danse(s)` permet de cocher plusieurs styles pour le même événement.

Pour les dates, utiliser le format `AAAA-MM-JJ`, par exemple `2026-05-16`.

La récupération automatique de l'image officielle Facebook n'est pas encore branchée. Pour l'instant, l'utilitaire gère l'import manuel d'une image locale compatible avec le site.

L'utilitaire écrit maintenant deux fichiers synchronisés :

```text
data/evenements.json
data/evenements.js
```

Le fichier `.js` sert de secours si un navigateur bloque la lecture directe du JSON.
