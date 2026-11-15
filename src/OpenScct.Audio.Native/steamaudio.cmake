# Valve's official binary SDK, pinned by archive hash. Only CPU binaural DSP is
# used; no GPU, simulation or device backend is initialized.
FetchContent_Declare(steamaudio
    URL https://github.com/ValveSoftware/steam-audio/releases/download/v4.8.1/steamaudio_4.8.1.zip
    URL_HASH SHA256=4a0aa5ec1176f38f0b0993a37c2259d9e86f27e22d5e24f83ec4c3cb9a1d5449
    DOWNLOAD_EXTRACT_TIMESTAMP TRUE)
FetchContent_MakeAvailable(steamaudio)
add_library(scct_steamaudio SHARED IMPORTED)
if(WIN32)
    set(SCCT_PHONON "${steamaudio_SOURCE_DIR}/lib/windows-x64/phonon.dll")
    set_target_properties(scct_steamaudio PROPERTIES IMPORTED_IMPLIB "${steamaudio_SOURCE_DIR}/lib/windows-x64/phonon.lib")
else()
    set(SCCT_PHONON "${steamaudio_SOURCE_DIR}/lib/linux-x64/libphonon.so")
endif()
set_target_properties(scct_steamaudio PROPERTIES IMPORTED_LOCATION "${SCCT_PHONON}"
    INTERFACE_INCLUDE_DIRECTORIES "${steamaudio_SOURCE_DIR}/include")
install(FILES "${SCCT_PHONON}" DESTINATION "bin/${SCCT_AUDIO_PLATFORM}")
install(FILES "${CMAKE_CURRENT_SOURCE_DIR}/LICENSE-SteamAudio.txt" DESTINATION licenses)
install(FILES "${steamaudio_SOURCE_DIR}/THIRDPARTY.md" DESTINATION licenses RENAME SteamAudio-THIRDPARTY.md)
