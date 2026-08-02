# Emby Edge

Emby Edge is a full-panel-first library browser and video player for XENEON
EDGE. Its left third contains user, library, scope, and search controls;
the right two thirds contain browsing, item details, media options, and video
playback.

iCUE currently shows its standard S/M/L/XL selector for every XENEON widget;
the widget manifest format has no documented way to remove individual sizes.
Emby Edge fills the complete allocation at XL and keeps the same proportional
composition at other sizes instead of changing the hardware layout.

## Setup

1. Install and start Edge Companion.
2. Add Emby Edge to an XL/full-panel slot.
3. In widget settings, set **Emby server**. The default is
   `http://192.168.1.11:8096/`.
4. Select a visible Emby user or type a username, then sign in.

The access token issued by Emby is stored in that widget's local storage. The
password is used only for sign-in and is not stored. Selecting the account at
the bottom of the library pane signs out and removes the stored token.

## Emby synchronization

- Libraries, folders, search results, metadata, art, and user play state come
  directly from the configured Emby server.
- Playback start, progress, pause, seek, and stop events are reported to Emby.
- Progress is checked in every 10 seconds while playing.
- Watched and unwatched changes are written to the selected Emby user.
- Emby's playback-info response supplies media versions and audio/subtitle
  tracks before playback.
- The Version selector appears only when Emby reports multiple media sources.
  Numeric or year-like source names are replaced with available resolution,
  codec, and container details.
- Video playback uses an Emby-generated progressive WebM compatibility stream
  with VP8-family video (`vpx`) and Vorbis audio. Those open codecs match the
  Windows codec support documented for iCUE widgets; H.264 is intentionally not
  used. The selected audio track is passed to Emby, and selected subtitles are
  burned into the compatibility stream.
- Playback is considered started only after decoded frames advance the video
  clock. If that does not happen, the player displays decoder and network state
  diagnostics instead of leaving a silent black screen at 0:00.
- Moving the pointer or touching the video reveals the playback toolbar. It
  remains visible while paused or keyboard-focused and hides after a short
  period of inactivity while playing. The toolbar provides previous/next item,
  play/pause, mute, volume, and caption controls.
- Previous and next follow the playable titles in the current browser results.
  Changing captions restarts Emby's compatibility stream at the current
  position because subtitles are burned into that stream.

The companion accepts only loopback, RFC1918 private IPv4, and local IPv6 Emby
addresses and does not follow redirects. Compatibility transcoding requires
the Emby server's transcoder and may use more server CPU than direct streaming.

## Playback troubleshooting

If playback opens to a black screen at 0:00, first confirm the widget header
shows version 1.0.3 or newer. Older builds requested H.264/AAC even though the
official iCUE Windows widget documentation lists AV1, VP8, and VP9 video codec
support. Current builds request VP8-family WebM and show the reported codec,
decoder state, network state, media error code, and decoded frame dimensions if
the clock does not advance within 10 seconds.
