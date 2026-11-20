extern unsigned *__cdecl name_crc(unsigned *destination, const char *name);

__declspec(selectany) const char name_7dae6c[] = "P_ChatIngame";
__declspec(selectany) const char name_7daf00[] = "L_Title";
__declspec(selectany) const char name_7df018[] = "P_MenuGameOver";
__declspec(selectany) const char name_7df038[] = "P_Pause";
__declspec(selectany) const char name_7df888[] = "Lst_menu";
__declspec(selectany) const char name_7df894[] = "P_GameOverOnline";
__declspec(selectany) const char name_7dfa60[] = "L_Infos";
__declspec(selectany) const char name_7dfbc4[] = "Lst_Menu";
__declspec(selectany) const char name_7dfeac[] = "B_Back";
__declspec(selectany) const char name_7e11a0[] = "B_Accept";
__declspec(selectany) const char name_7e1580[] = "L_Chat";
__declspec(selectany) const char name_7e1588[] = "A_Chat";
__declspec(selectany) const char name_7e25ec[] = "P_MenuOnline";
__declspec(selectany) const char name_7e2684[] = "P_ManageGame";
__declspec(selectany) const char name_7e2f30[] = "B_LoadGame";
__declspec(selectany) const char name_7e3014[] = "P_MenuRoomCreateLan";
__declspec(selectany) const char name_7e32c4[] = "P_MenuCoop";
__declspec(selectany) const char name_7e33e0[] = "B_Exit";
__declspec(selectany) const char name_7e35bc[] = "P_MusicsBio";
__declspec(selectany) const char name_7e3708[] = "P_Extras";
__declspec(selectany) const char name_7e37ec[] = "P_TrainingVideo";
__declspec(selectany) const char name_7e38ec[] = "Lst_Video";
__declspec(selectany) const char name_7e38f8[] = "P_TrainingVideoMain";
__declspec(selectany) const char name_7e4544[] = "B_Reset";
__declspec(selectany) const char name_7e454c[] = "B_Save";
__declspec(selectany) const char name_7e604c[] = "L_OverText";
__declspec(selectany) const char name_7e6058[] = "A_OverArea";
extern unsigned id_92d54c;
extern unsigned id_92d550;
extern unsigned id_92d700;
extern unsigned id_92d704;
extern unsigned id_92d75c;
extern unsigned id_92d760;
extern unsigned id_92d890;
extern unsigned id_92d894;
extern unsigned id_92d8c0;
extern unsigned id_92d8c4;
extern unsigned id_92dad8;
extern unsigned id_92dadc;
extern unsigned id_92db08;
extern unsigned id_92db0c;
extern unsigned id_92dd98;
extern unsigned id_92dd9c;
extern unsigned id_92de38;
extern unsigned id_92de3c;
extern unsigned id_92df04;
extern unsigned id_92df08;
extern unsigned id_92df34;
extern unsigned id_92df38;
extern unsigned id_92df64;
extern unsigned id_92df68;
extern unsigned id_92df94;
extern unsigned id_92df98;
extern unsigned id_92e428;
extern unsigned id_92e42c;
extern unsigned id_92e4ac;
extern unsigned id_92e4b0;
extern unsigned id_92e4dc;
extern unsigned id_92e4e0;

#define REGISTER_PAIR(Entry, FirstID, FirstName, SecondID, SecondName) \
    void __cdecl Entry(void) \
    { \
        unsigned value; \
        FirstID = *name_crc(&value, FirstName); \
        SecondID = *name_crc(&value, SecondName); \
    }

REGISTER_PAIR(register_pair_227f50, id_92d54c, name_7df894, id_92d550, name_7df888)
REGISTER_PAIR(register_pair_237010, id_92d700, name_7df038, id_92d704, name_7df888)
REGISTER_PAIR(register_pair_237e60, id_92d75c, name_7df018, id_92d760, name_7df888)
REGISTER_PAIR(register_pair_23c7c0, id_92d890, name_7e1588, id_92d894, name_7e1580)
REGISTER_PAIR(register_pair_23d3d0, id_92d8c0, name_7dae6c, id_92d8c4, name_7e1580)
REGISTER_PAIR(register_pair_245f80, id_92dad8, name_7e25ec, id_92dadc, name_7df888)
REGISTER_PAIR(register_pair_246420, id_92db08, name_7e2684, id_92db0c, name_7e11a0)
REGISTER_PAIR(register_pair_250e80, id_92dd98, name_7e3014, id_92dd9c, name_7e2f30)
REGISTER_PAIR(register_pair_252370, id_92de38, name_7e32c4, id_92de3c, name_7df888)
REGISTER_PAIR(register_pair_253590, id_92df04, name_7e35bc, id_92df08, name_7dfa60)
REGISTER_PAIR(register_pair_253bd0, id_92df34, name_7e3708, id_92df38, name_7dfbc4)
REGISTER_PAIR(register_pair_253ec0, id_92df64, name_7e37ec, id_92df68, name_7daf00)
REGISTER_PAIR(register_pair_2543a0, id_92df94, name_7e38f8, id_92df98, name_7e38ec)
REGISTER_PAIR(register_pair_264720, id_92e428, name_7e454c, id_92e42c, name_7e4544)
REGISTER_PAIR(register_pair_2658e0, id_92e4ac, name_7dfeac, id_92e4b0, name_7e33e0)
REGISTER_PAIR(register_pair_265c60, id_92e4dc, name_7e6058, id_92e4e0, name_7e604c)
