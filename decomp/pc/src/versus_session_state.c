typedef struct Object Object;
struct Frame { unsigned char prefix[12]; unsigned char *code; };
#define HANDLER(name, type) \
    void __fastcall name(Object *self, unsigned unused_edx, struct Frame *frame, type *result)
extern int lan_mode;
extern Object *lan_error_handler;
extern unsigned server_locked, server_punkbuster, server_version;
struct PendingClients { unsigned char prefix[8]; unsigned char accepting; unsigned char padding[7]; int count; };
extern struct PendingClients *pending_clients;

HANDLER(exec_Engine_Actor_LeaveLAN, void)
{
    ++frame->code;
    lan_mode = 0;
}

HANDLER(exec_Engine_Actor_EnterLAN, void)
{
    ++frame->code;
    lan_mode = 1;
}

HANDLER(exec_SBase_SLANServerLink_Init, void)
{
    ++frame->code;
}

HANDLER(exec_SBase_SRJ45Checker_isRJ45Plugged, int)
{
    ++frame->code;
    *result = 1;
}

HANDLER(exec_Engine_LANErrorHandler_Initialize, void)
{
    ++frame->code;
    lan_error_handler = self;
}

HANDLER(exec_SBase_SPlayerController_resetClientPending, void)
{
    ++frame->code;
    pending_clients->count = 0;
}

HANDLER(exec_SBase_SPlayerController_refuseClientsConnections, void)
{
    ++frame->code;
    pending_clients->accepting = 0;
}

HANDLER(exec_Engine_Actor_NetType, unsigned char)
{
    ++frame->code;
    *result = (unsigned char)lan_mode;
}

HANDLER(exec_SBase_SXBoxLiveRef_IPJoinIsLocked, unsigned)
{
    ++frame->code;
    *result = server_locked;
}

HANDLER(exec_SBase_SXBoxLiveRef_IPJoinPunkBusterEnabled, unsigned)
{
    ++frame->code;
    *result = server_punkbuster;
}

HANDLER(exec_SBase_SXBoxLiveRef_IPJoinGetServerVersion, unsigned)
{
    ++frame->code;
    *result = server_version;
}

HANDLER(exec_SBase_SPlayerController_canStartTravelOnServer, int)
{
    ++frame->code;
    *result = pending_clients->count == 0;
}

HANDLER(exec_SBase_SPlayerController_NbPendingClientOnServer, int)
{
    ++frame->code;
    *result = pending_clients->count;
}
