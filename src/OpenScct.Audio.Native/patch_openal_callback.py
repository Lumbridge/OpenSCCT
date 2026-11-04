"""Apply the bounded unity-rate callback adapter to pinned OpenAL Soft 1.24.3.

No reverb coefficients or DSP change. At unity rate the mixer copies samples
directly, so resampler lookahead is unused. Do not request unavailable future
samples from a synchronous effect's callback. A still-live callback with no
cached samples must remain playable for the following mixer block.
"""
from pathlib import Path
import sys

path = Path(sys.argv[1])
text = path.read_text(encoding="utf-8")
changes = [
    (
        "const size_t needSamples{bufferOffset + srcBufferSize - srcSampleDelay};",
        "// OpenSCCT synchronous callback adapter: unity-rate direct copy needs no lookahead.\n"
        "                const bool scctDirectCopy{increment == MixerFracOne && fracPos == 0 && mDecoderPadding == 0};\n"
        "                const size_t needSamples{bufferOffset + (scctDirectCopy ? dstBufferSize : srcBufferSize) - srcSampleDelay};",
    ),
    (
        "if(blocksDone < mNumCallbackBlocks)",
        "if(blocksDone < mNumCallbackBlocks || (blocksDone == mNumCallbackBlocks\n"
        "                && !mFlags.test(VoiceCallbackStopped)))",
    ),
]
for original, replacement in changes:
    if replacement in text:
        continue
    if text.count(original) != 1:
        raise SystemExit(f"Pinned OpenAL callback patch context mismatch: {original}")
    text = text.replace(original, replacement, 1)
path.write_text(text, encoding="utf-8", newline="\n")
