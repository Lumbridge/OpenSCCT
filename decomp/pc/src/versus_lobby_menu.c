#include "versus_script_frame.h"

struct LobbyService;
union LobbyMethod {
    int (__fastcall *query)(struct LobbyService *);
    void (__fastcall *action)(struct LobbyService *);
};
struct LobbyService { const union LobbyMethod *methods; };

struct GameEngine {
    unsigned char before_online_service[0x40];
    struct LobbyService *online;
};
extern struct GameEngine *game_engine;

#define LOBBY_QUERY(name, method) \
    void __fastcall name(void *self, unsigned unused_edx, struct VersusScriptFrame *frame, int *result) \
    { \
        struct LobbyService *service; \
        ++frame->code; \
        service = game_engine->online; \
        *result = service->methods[method].query(service); \
    }

#define LOBBY_ACTION(name, method) \
    void __fastcall name(void *self, unsigned unused_edx, struct VersusScriptFrame *frame, void *result) \
    { \
        struct LobbyService *service; \
        ++frame->code; \
        service = game_engine->online; \
        service->methods[method].action(service); \
    }

LOBBY_QUERY(execGetUserCount, 36)
LOBBY_QUERY(execGetLobbyCount, 47)
LOBBY_ACTION(execLeaveLobby, 50)
LOBBY_ACTION(execMatchQueryLeaveMatch, 84)
LOBBY_ACTION(execMatchConnected, 86)
LOBBY_QUERY(execGetResultsCount, 90)
LOBBY_ACTION(execTerminateMatch, 93)
LOBBY_ACTION(execUpdateMatch, 94)
LOBBY_QUERY(execMatchGetAvailablePublicSlots, 115)
LOBBY_QUERY(execMatchGetAvailablePrivateSlots, 116)
LOBBY_QUERY(execBlackListGetNum, 126)
LOBBY_QUERY(execGetNbFriendsInList, 124)
LOBBY_ACTION(execGameIsFinished, 148)
LOBBY_ACTION(execCurrentInvitationDelete, 144)
LOBBY_QUERY(execGetFriendLobbyIndex, 136)
LOBBY_QUERY(execGetLastIndexFriendUpdated, 133)
LOBBY_QUERY(execGetLastIndexInvitationUpdated, 143)
LOBBY_QUERY(execGetInvitationSearchedIndex, 146)
LOBBY_ACTION(execServerStartGame, 138)
LOBBY_QUERY(execIsGameInvitePending, 125)
LOBBY_ACTION(execSendGameInviteList, 151)
LOBBY_QUERY(execGetInviteCount, 172)
LOBBY_QUERY(execRecentPlayerGetNum, 174)
LOBBY_QUERY(execLadderDyn_GetPlayerFinishedGamesRatio, 183)
LOBBY_QUERY(execServerIsReachable, 110)
LOBBY_QUERY(execGetServerPublicPort, 26)
LOBBY_QUERY(execIsLocked, 195)
