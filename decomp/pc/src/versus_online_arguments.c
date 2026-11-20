#include "versus_vm_argument.h"

struct OnlineService;
struct StackInt { int value; };
union OnlineMethod {
    int (__fastcall *query)(struct OnlineService *, struct StackInt);
    void (__fastcall *action)(struct OnlineService *, struct StackInt);
};
struct OnlineService { const union OnlineMethod *methods; };
struct GameEngine { unsigned char before_service[0x40]; struct OnlineService *online; };
extern struct GameEngine *game_engine;

static __forceinline struct OnlineService *finish_argument(struct ArgumentFrame *frame)
{
    const unsigned char *cursor = ((volatile struct ArgumentFrame *)frame)->code;
    struct GameEngine *engine = game_engine;
    frame->code = cursor + 1;
    return engine->online;
}

#define ONLINE_QUERY(Entry, Slot) \
    void __fastcall Entry(void *self, unsigned unused_edx, struct ArgumentFrame *frame, int *result) \
    { \
        struct StackInt argument = {0}; \
        struct OnlineService *service; \
        read_argument(frame, &argument.value); \
        service = finish_argument(frame); \
        *result = service->methods[Slot].query(service, argument); \
    }

#define ONLINE_ACTION(Entry, Slot) \
    void __fastcall Entry(void *self, unsigned unused_edx, struct ArgumentFrame *frame, void *result) \
    { \
        struct StackInt argument = {0}; \
        struct OnlineService *service; \
        read_argument(frame, &argument.value); \
        service = finish_argument(frame); \
        service->methods[Slot].action(service, argument); \
    }

ONLINE_QUERY(execLadderSta_GetPlayersCount, 188)
ONLINE_QUERY(execLadderDyn_GetPlayersCount, 182)
ONLINE_QUERY(execHasSentRequestTo, 167)
ONLINE_QUERY(execHasReceivedRequestFrom, 166)
ONLINE_QUERY(execHasSentInviteTo, 165)
ONLINE_QUERY(execHasReceivedInviteFrom, 164)
ONLINE_QUERY(execFriendHasVoice, 163)
ONLINE_QUERY(execFriendIsJoinable, 162)
ONLINE_QUERY(execFriendIsPlaying, 161)
ONLINE_QUERY(execFriendIsOnline, 160)
ONLINE_ACTION(execRemoveRedundantInvitation, 154)
ONLINE_ACTION(execRemoveFriend, 158)
ONLINE_ACTION(execFriendCancelGameInvite, 153)
ONLINE_ACTION(execAddToGameInviteList, 150)
ONLINE_ACTION(execOpenGameInviteList, 149)
ONLINE_ACTION(execSendInvitationToAFriend, 141)
ONLINE_QUERY(execIsFriendAlreadyInRoom, 140)
ONLINE_QUERY(execGetFriendIDLobby, 132)
ONLINE_QUERY(execGetFriendIDRoom, 131)
ONLINE_QUERY(execGetPlayerStatus, 139)
ONLINE_QUERY(execGetFriendStatus, 130)
ONLINE_QUERY(execGetInvitationLobbyIndex, 142)
ONLINE_ACTION(execSetJoinFromFriend, 137)
ONLINE_ACTION(execGetGroupInfo, 135)
ONLINE_ACTION(execNotifyPlayerDisconnection, 89)
ONLINE_ACTION(execMatchFinished, 88)
ONLINE_ACTION(execStartMatch, 87)
ONLINE_ACTION(execMatchSetPlayerTime, 123)
ONLINE_ACTION(execMatchSetAlternateMode, 119)
ONLINE_ACTION(execMatchSetTutoratMode, 117)
ONLINE_ACTION(execMatchFillPrivateSlots, 114)
ONLINE_ACTION(execMatchFillPublicSlots, 113)
ONLINE_ACTION(execMatchSetNbPrivateSlots, 112)
ONLINE_ACTION(execMatchSetNbPublicSlots, 111)
ONLINE_ACTION(execMatchSetGameState, 106)
ONLINE_ACTION(execMatchSetGameLevelGap, 105)
ONLINE_ACTION(execMatchSetGameLevel, 104)
ONLINE_ACTION(execMatchSetbGamesInARowPlaying, 103)
ONLINE_ACTION(execMatchSetGameMode, 101)
ONLINE_ACTION(execMatchSetMapAmateur, 100)
ONLINE_ACTION(execMatchSetServerMaxPlayers, 97)
ONLINE_QUERY(execMatchQueryGetPing, 82)
ONLINE_QUERY(execMatchQueryIsReachable, 83)
ONLINE_QUERY(execMatchQueryGetFinishedGamesRatio, 61)
ONLINE_QUERY(execMatchQueryGetNumMatch, 53)
ONLINE_ACTION(execRefreshServerListOnLobby, 51)
ONLINE_ACTION(execJoinLobby, 49)
ONLINE_QUERY(execGetLobbyUserCount, 48)
ONLINE_ACTION(execRemoveAccount, 44)
ONLINE_QUERY(execIsUserPasscodeNeeded, 38)
