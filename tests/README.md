# Browser regression fixtures

These small pages support manual browser checks for Emby Edge without using a
real library item or account token.

## Video and audio playback

Use a short WebM fixture containing VP8 video and Vorbis audio, then start the
mock Emby stream endpoint:

```powershell
$env:EMBY_EDGE_TEST_MEDIA = 'C:\path\to\vp8-vorbis.webm'
node tests/emby-mock-server.mjs
```

Open `tests/emby-playback.html` in a browser and select **Start AV test**. The
page sends the stream through Edge Companion on port `48620`, exercising range
requests and the same VP8/Vorbis compatibility path used by the widget.

## Details scrolling

Open `tests/emby-details-scroll.html` at a `1688 × 696` viewport. Confirm the
details pane scrolls vertically far enough to reach its media selectors and
Play button without changing the one-third/two-thirds panel composition.
