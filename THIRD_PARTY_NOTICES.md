# Third-party components and licensing

StudioCut invokes FFmpeg and FFprobe as separate external executables.

The Windows Actions workflow downloads FFmpeg Essentials builds from Gyan.dev (https://www.gyan.dev/ffmpeg/builds/), which include independently copyrighted components and codecs. Those executables remain governed by their own applicable licenses (see https://ffmpeg.org/legal.html and the Gyan.dev build details). Distributors must include applicable license texts, provide corresponding source and fulfill any other GPL/LGPL or codec-license obligations for the exact build they ship. The workflow copies the GPL 3.0 license into the distribution and an FFmpeg build license file if one is present; this notice is not a substitute for legal compliance.

StudioCut does not claim ownership of FFmpeg and is not affiliated with FFmpeg or Gyan.dev.
