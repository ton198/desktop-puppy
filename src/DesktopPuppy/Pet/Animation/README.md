# Packaged Snoopy runtime animations

PNG frames are copied from assets/snoopy/frames, p0-mcp-20261004/review-frames and greeting-special-v2-20261004/review-frames. Latest reviewed frames take precedence. Original images and alignment intermediates remain source material. The rejected glance-entry alternative is excluded.

sequences.json preserves the v2 action timings with preview-only opening/closing idle holds removed. Other actions use short runtime timings. PetWindow loads frozen images once, shares the same crop across poses and advances using a monotonic clock. Drag is held until the native move operation ends. No image generation occurs at runtime.
