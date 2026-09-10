# Screenshots

Every `.png` / `.jpg` here becomes a gallery image on the mod.io page, pushed by
`scripts/modio-page.sh` on each release. Removing one from this folder removes it from the
page.

- **1920x1080** is what the store shows them at; 512x288 is the floor and 8MB the
  ceiling.
- Named for what they show — `chest-search.png`, not `Screenshot_20260906.png`. The
  name is what mod.io stores and what the sync matches on.
- **Editing a shot in place does nothing.** The sync compares filenames, so a
  changed image under the same name is treated as already uploaded. Rename it.
