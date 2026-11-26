# Application icons

The icon assets in this directory are the source assets used for desktop installer bundles.

To regenerate the platform icon set from artwork you are authorized to use, run:

```bash
node_modules/.bin/tauri icon path/to/your-own-logo.png
```

Build an installer from the `aether-desktop` directory with `yarn build` after generating icons.
