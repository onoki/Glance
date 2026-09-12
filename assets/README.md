# Glance app icon

`icon.svg` is the scalable source artwork. Its canvas outside the checklist page is transparent.

On Windows, regenerate the raster and application-icon variants from the repository root:

```powershell
.\assets\generate-icon.ps1
```

The script renders the same vector geometry directly at each target size. It writes a transparent
512 px `icon.png`, a multi-resolution `icon.ico` with 16–256 px frames, and copies the three web
assets into `ui/public`. It also verifies PNG corner transparency and the ICO frame count.
