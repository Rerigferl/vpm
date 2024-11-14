# Rerigferl's Vpm Repository

## Content
- enhanced-blendshape-editor
- shader-fallback-overwriter
- modular-avatar-copy-scale-adjuster

## Repository Builder
```
      - uses: "docker://ghcr.io/rerigferl/vpm-repository-builder:v2.0.0"
        with:
          args: "source.txt ${{ github.token }} repository-settings.json"
```
