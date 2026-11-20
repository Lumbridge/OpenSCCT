#include "static_methods_reset.h"

extern const void *const methods_7b7e68[];
extern const void *const methods_7bf388[];
extern const void *const methods_7c1d60[];
extern struct StaticRecord static_install_8c8c10;
extern struct StaticRecord static_install_8cf408;
extern struct StaticRecord static_install_8f5fac;

CLEANUP(cleanup_install_7b0730, static_install_8f5fac, methods_7bf388)
CLEANUP(cleanup_install_7b3590, static_install_8c8c10, methods_7b7e68)
CLEANUP(cleanup_install_7b4f70, static_install_8cf408, methods_7c1d60)
