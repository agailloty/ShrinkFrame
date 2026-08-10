# Media processing

## Tooling

Bundle pinned Linux builds of `ffmpeg` and `ffprobe` in the Docker image. At startup, execute version probes and expose availability through health/details. The process runner is infrastructure code and never invokes a shell.

## Input probing

Use ffprobe JSON output to capture:

- container and duration;
- all streams and dispositions;
- primary video codec, pixel format, dimensions, sample aspect ratio, frame rate;
- rotation from display matrix/side data and relevant tags;
- audio codecs/channels/sample rates;
- capture date and QuickTime metadata;
- location metadata when present.

The application derives an effective orientation rather than relying only on width/height tags.

## FFmpeg process rules

- Construct arguments through `ProcessStartInfo.ArgumentList`.
- `UseShellExecute=false`; redirect stdout/stderr; no window.
- Use `-nostdin`, `-progress pipe:1`, and `-nostats` for machine progress.
- Read both streams concurrently and await both readers.
- Check exit code and retain a bounded diagnostic tail.
- On cancellation, kill the entire process tree, await exit, delete partial output, and preserve input.
- Use a `.partial` output then atomically finalize after validation.
- Concurrency and thread count are configuration, not hardcoded values.

## Output contract

- Container: MP4.
- Video: libx264 H.264.
- Pixel format: use broadly compatible `yuv420p` unless the validated source/policy explicitly requires otherwise; HDR behavior must be documented and may be rejected with a clear warning in the POC rather than silently damaged.
- Enable `+faststart` unconditionally.
- Map intended video/audio streams and global metadata deliberately.
- Do not accidentally include thumbnail/cover-art video streams as the primary video.

## Scaling

Maximum values represent the long display dimension and must work for landscape and portrait media. Never upscale. Ensure even encoded dimensions. Preserve display aspect ratio and effective orientation. Derive and unit-test the filter builder independently.

## Audio policy

Copy the selected primary audio stream when its codec is MP4-compatible under the implemented compatibility table. Otherwise encode AAC using a documented default bitrate. If stream copy fails for a case that was considered compatible, the job fails with an actionable message; do not silently rerun with changed settings unless that behavior is explicitly implemented and logged.

## Progress

Parse FFmpeg key/value progress. Report percentage, processed time, speed, elapsed time, estimated remaining time, FPS, bitrate, and current output size when available. Throttle database writes, for example to at most once per second, while allowing smoother in-memory UI updates.

## Validation

Output is accepted only when:

- FFmpeg exit code is zero;
- final ffprobe succeeds;
- exactly the intended primary video is present and encoded H.264;
- duration difference is no greater than `max(1 second, inputDuration * 0.005)`;
- dimensions are positive, even, within the selected maximum, and not upscaled;
- effective rotation matches the input presentation;
- authoritative capture date is retained;
- output file is nonempty and inside work storage.

Loss of capture date or rotation is blocking. Other lost metadata becomes a warning. A valid output with size greater than or equal to input becomes `NotBeneficial`; it is retained and may be downloaded or force-published.

## POC HDR limitation

The POC rejects video identified by ffprobe as PQ (`smpte2084`) or HLG (`arib-std-b67`). Its H.264 output policy is 8-bit `yuv420p`, and no validated tone-mapping or HDR metadata-preservation pipeline is configured. Converting such input would risk washed-out colors or clipped highlights. HDR support requires a later explicit color-management policy and validation fixtures; it must not be enabled by merely removing the rejection.

## Repeatable manual checks

Use generated test patterns only; do not use personal media. The commands below are PowerShell and deliberately include spaces and shell metacharacters in filenames.

```powershell
$fixtureRoot = Join-Path (Resolve-Path .) '.local/media-check'
New-Item -ItemType Directory -Force -Path $fixtureRoot | Out-Null
$inputPath = Join-Path $fixtureRoot 'fixture input & safe [x].mov'
$outputPath = Join-Path $fixtureRoot 'result & safe.partial.mp4'

& ffmpeg -hide_banner -loglevel error -y -f lavfi -i 'testsrc2=size=640x360:rate=30:duration=3' -f lavfi -i 'sine=frequency=1000:sample_rate=48000:duration=3' -map 0:v:0 -map 1:a:0 -c:v libx264 -pix_fmt yuv420p -c:a pcm_s16le -metadata creation_time='2024-01-02T03:04:05Z' $inputPath
& ffprobe -v error -print_format json -show_format -show_streams $inputPath
& ffmpeg -hide_banner -nostdin -nostats -y -noautorotate -i $inputPath -map 0:0 -map 0:1 -map_metadata 0 -map_chapters 0 -c:v libx264 -preset medium -crf 24 -pix_fmt yuv420p -vf 'scale=480:270:flags=lanczos' -metadata:s:v:0 rotate=0 -c:a aac -b:a 192k -movflags +faststart -progress pipe:1 -f mp4 $outputPath
& ffprobe -v error -select_streams v:0 -show_entries format=format_name,duration,size:stream=codec_name,width,height,pix_fmt -of json $outputPath
```

Run the cancellation coverage, which generates a 30-second synthetic fixture, cancels the typed adapter during a `veryslow` encode, and asserts that the process has exited and the partial file is absent:

```powershell
dotnet test tests/ShrinkFrame.Infrastructure.Tests/ShrinkFrame.Infrastructure.Tests.csproj --configuration Release --filter Compressor_cancellation_awaits_exit_and_removes_partial_output
```
