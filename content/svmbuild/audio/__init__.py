"""Audio decoders and writers for the audio steps (spec/native/audio.md, docs/AUDIO.md).

- sound: the Sound exports of the .uax packages (UNIAudio's own layout, not stock UE2)
- uas: the per-map stream banks (.uas): package sections, entry index, Ogg Vorbis streams
- ogg: Ogg page walking, page CRCs and Vorbis stream facts
- mux: the .mux channel maps of the multichannel sounds
- refs: sound references of map actors, zone effects (I3DL2 reverb) and class defaults
- build: the shared library (shared/audio) and the per-map bank (svmap/<Map>/audio)
"""
