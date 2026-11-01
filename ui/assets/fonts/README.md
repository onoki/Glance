# IBM Plex Sans

Glance bundles one font family, IBM Plex Sans, unmodified under SIL Open Font License 1.1. See `IBMPlexSans-OFL.txt` for copyright, attribution and license terms. The old BigBlue font is no longer used or distributed.

The upright face is the same file used in the user's accepted interactive comparison. Both files have weight 100–700 and width 75–100 variable axes. Glance uses normal width and regular 400 UI text; bold 700 and italic task marks use genuine faces, including combined bold italic. Font synthesis is disabled. No outline modification, conversion or subsetting was performed.

Sources:

- Typeface: https://www.ibm.com/design/language/typography/typeface/
- Distribution: https://github.com/google/fonts/tree/main/ofl/ibmplexsans
- `IBMPlexSans-Regular.ttf`: distribution file `IBMPlexSans[wdth,wght].ttf`.
- `IBMPlexSans-Italic.ttf`: distribution file `IBMPlexSans-Italic[wdth,wght].ttf`.
- `IBMPlexSans-OFL.txt`: distribution file `OFL.txt`.

SHA-256:

```text
3b031aa4216174205bd8471f88a49b91f093169e9e87bd5262242bc5967fe2e3  IBMPlexSans-Regular.ttf
0b94c5e981993764db32bf9c610ecc60cbd34ad77ec2f10ba03c64ab75124d8e  IBMPlexSans-Italic.ttf
```

CSS font URLs are processed by Vite into build assets. Settings imports the license as a non-inlined URL so the portable package includes its attribution. The shared typography and palette are documented in `ui/AGENTS.md` and `docs/requirements.feature`.
