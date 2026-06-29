# FFmpeg ASS Subtitle Fonts

FFmpeg's `ass` filter uses [libass](https://github.com/libass/libass) for subtitle rendering. Fonts are resolved at render time — the `.ass` file only names them.

## fontsdir (recommended)

Point libass to a directory containing `.ttf`/`.otf` files. No system install needed.

```bash
ffmpeg -i video.mp4 \
  -vf "ass=subs.ass:fontsdir=/path/to/fonts" \
  output.mp4
```

The filter will find any font by name inside that directory. This works for **any output container** (MP4, MKV, etc.).

## MKV attachments (self-contained)

Embed fonts into a Matroska container so they travel with the file:

```bash
ffmpeg -i video.mp4 -i subs.ass \
  -attach /path/to/ChangaOne-Regular.ttf \
    -metadata:s:t:mimetype=application/x-truetype-font \
  -attach /path/to/ChangaOne-Bold.ttf \
    -metadata:s:t:mimetype=application/x-truetype-font \
  -c copy output.mkv
```

Players like VLC and mpv will use the attached fonts automatically. **Only works with MKV.**

## Common pitfalls

| Issue | Fix |
|---|---|
| Font rendered as Arial/default | Font name in ASS must match the font's internal `name` table exactly. Check with `fc-scan` or `python3 -c "from fontTools import ttLib; f=ttLib.TTFont('font.ttf'); print(f['name'].getName(1,3,1,0x0409))"` |
| Font not found in `fontsdir` | Use an **absolute path** for `fontsdir`. Relative paths can fail depending on FFmpeg's working directory. |
| Fallback glyphs look wrong | libass doesn't do font fallback chains well. Ensure your font covers all characters used in subtitles. |
| Bold/italic variants | If the ASS style sets `Bold: 1`, libass looks for **the same font name** with a bold face. Include regular + bold `.ttf` files. |

## Checking font availability

```bash
# List fonts libass can find in a directory
ffmpeg -v debug -vf "ass=subs.ass:fontsdir=/path/to/fonts" -i input.mp4 -f null - 2>&1 | grep -i font
```

## Quick test

```bash
ffmpeg -f lavfi -i color=c=black:s=1080x1920:d=10 \
  -vf "ass=test-subs.ass:fontsdir=./fonts" \
  -c:v libx264 -pix_fmt yuv420p output.mp4
```
