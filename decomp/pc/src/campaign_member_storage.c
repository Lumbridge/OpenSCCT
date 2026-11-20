struct Record { unsigned int first_word; void *storage; };
extern void *__cdecl get_release_context(void);
extern void __cdecl release_record_storage(void *context, void *storage, unsigned int kind);

#define CLEAR_RECORD(Entry, Clear, Kind) \
    void __fastcall Entry(struct Record *record) \
    { \
        Clear(record); \
        release_record_storage(get_release_context(), record->storage, Kind); \
        record->storage = 0; \
    }

extern void __fastcall clear_contents_060600(struct Record *record);
extern void __fastcall clear_contents_060740(struct Record *record);
extern void __fastcall clear_contents_1bca40(struct Record *record);
extern void __fastcall clear_contents_204540(struct Record *record);
extern void __fastcall clear_contents_47eb20(struct Record *record);
extern void __fastcall clear_contents_481300(struct Record *record);
extern void __fastcall clear_contents_48c120(struct Record *record);
extern void __fastcall clear_contents_48c520(struct Record *record);
extern void __fastcall clear_contents_494500(struct Record *record);
extern void __fastcall clear_contents_494c50(struct Record *record);
extern void __fastcall clear_contents_49a6e0(struct Record *record);
extern void __fastcall clear_contents_4a14f0(struct Record *record);
extern void __fastcall clear_contents_4a15d0(struct Record *record);
extern void __fastcall clear_contents_4a7f90(struct Record *record);
extern void __fastcall clear_contents_4b1950(struct Record *record);
extern void __fastcall clear_contents_4b68f0(struct Record *record);
extern void __fastcall clear_contents_4b92d0(struct Record *record);
extern void __fastcall clear_contents_4b9760(struct Record *record);
extern void __fastcall clear_contents_4ba430(struct Record *record);
extern void __fastcall clear_contents_4bdfb0(struct Record *record);
extern void __fastcall clear_contents_4c28b0(struct Record *record);
extern void __fastcall clear_contents_4c57c0(struct Record *record);
extern void __fastcall clear_contents_4c6c60(struct Record *record);
extern void __fastcall clear_contents_4cd100(struct Record *record);
extern void __fastcall clear_contents_4cf350(struct Record *record);
extern void __fastcall clear_contents_4d5e50(struct Record *record);
extern void __fastcall clear_contents_4db7e0(struct Record *record);
extern void __fastcall clear_contents_4dd0d0(struct Record *record);
extern void __fastcall clear_contents_4de400(struct Record *record);
extern void __fastcall clear_contents_4e2f60(struct Record *record);
extern void __fastcall clear_contents_4e3040(struct Record *record);
extern void __fastcall clear_contents_4e4a30(struct Record *record);
extern void __fastcall clear_contents_4e6ae0(struct Record *record);
extern void __fastcall clear_contents_4f7900(struct Record *record);
extern void __fastcall clear_contents_73ac60(struct Record *record);

CLEAR_RECORD(clear_record_060870, clear_contents_060600, 7)
CLEAR_RECORD(clear_record_0617f0, clear_contents_060600, 7)
CLEAR_RECORD(clear_record_061ed0, clear_contents_060600, 7)
CLEAR_RECORD(clear_record_1bca80, clear_contents_1bca40, 7)
CLEAR_RECORD(clear_record_1bcab0, clear_contents_1bca40, 7)
CLEAR_RECORD(clear_record_1bcae0, clear_contents_1bca40, 7)
CLEAR_RECORD(clear_record_204480, clear_contents_060740, 7)
CLEAR_RECORD(clear_record_204510, clear_contents_060740, 7)
CLEAR_RECORD(clear_record_204590, clear_contents_204540, 7)
CLEAR_RECORD(clear_record_204630, clear_contents_060740, 7)
CLEAR_RECORD(clear_record_204660, clear_contents_204540, 7)
CLEAR_RECORD(clear_record_204710, clear_contents_204540, 7)
CLEAR_RECORD(clear_record_481460, clear_contents_481300, 7)
CLEAR_RECORD(clear_record_481b10, clear_contents_481300, 7)
CLEAR_RECORD(clear_record_481c10, clear_contents_47eb20, 7)
CLEAR_RECORD(clear_record_481c80, clear_contents_481300, 7)
CLEAR_RECORD(clear_record_482280, clear_contents_47eb20, 7)
CLEAR_RECORD(clear_record_482380, clear_contents_47eb20, 7)
CLEAR_RECORD(clear_record_48c2b0, clear_contents_48c120, 7)
CLEAR_RECORD(clear_record_48c7f0, clear_contents_48c120, 7)
CLEAR_RECORD(clear_record_48c960, clear_contents_48c520, 7)
CLEAR_RECORD(clear_record_48ca40, clear_contents_48c120, 7)
CLEAR_RECORD(clear_record_48ce20, clear_contents_48c520, 7)
CLEAR_RECORD(clear_record_48cf40, clear_contents_48c520, 7)
CLEAR_RECORD(clear_record_494620, clear_contents_494500, 7)
CLEAR_RECORD(clear_record_494a60, clear_contents_494500, 7)
CLEAR_RECORD(clear_record_494c10, clear_contents_494500, 7)
CLEAR_RECORD(clear_record_494ca0, clear_contents_494c50, 7)
CLEAR_RECORD(clear_record_494fe0, clear_contents_494c50, 7)
CLEAR_RECORD(clear_record_495090, clear_contents_494c50, 7)
CLEAR_RECORD(clear_record_49aae0, clear_contents_49a6e0, 7)
CLEAR_RECORD(clear_record_49ac50, clear_contents_49a6e0, 7)
CLEAR_RECORD(clear_record_49acd0, clear_contents_49a6e0, 7)
CLEAR_RECORD(clear_record_4a17c0, clear_contents_4a14f0, 7)
CLEAR_RECORD(clear_record_4a1840, clear_contents_4a15d0, 7)
CLEAR_RECORD(clear_record_4a1960, clear_contents_4a14f0, 7)
CLEAR_RECORD(clear_record_4a19e0, clear_contents_4a15d0, 7)
CLEAR_RECORD(clear_record_4a1a80, clear_contents_4a14f0, 7)
CLEAR_RECORD(clear_record_4a1ab0, clear_contents_4a15d0, 7)
CLEAR_RECORD(clear_record_4a8210, clear_contents_4a7f90, 7)
CLEAR_RECORD(clear_record_4a8450, clear_contents_4a7f90, 7)
CLEAR_RECORD(clear_record_4a9050, clear_contents_4a7f90, 7)
CLEAR_RECORD(clear_record_4b1b90, clear_contents_4b1950, 7)
CLEAR_RECORD(clear_record_4b1cb0, clear_contents_4b1950, 7)
CLEAR_RECORD(clear_record_4b1d30, clear_contents_4b1950, 7)
CLEAR_RECORD(clear_record_4b6b30, clear_contents_4b68f0, 7)
CLEAR_RECORD(clear_record_4b6d40, clear_contents_4b68f0, 7)
CLEAR_RECORD(clear_record_4b6de0, clear_contents_4b68f0, 7)
CLEAR_RECORD(clear_record_4b9570, clear_contents_4b92d0, 7)
CLEAR_RECORD(clear_record_4b97d0, clear_contents_4b92d0, 7)
CLEAR_RECORD(clear_record_4b98d0, clear_contents_4b9760, 7)
CLEAR_RECORD(clear_record_4b9930, clear_contents_4b92d0, 7)
CLEAR_RECORD(clear_record_4b9a40, clear_contents_4b9760, 7)
CLEAR_RECORD(clear_record_4b9ad0, clear_contents_4b9760, 7)
CLEAR_RECORD(clear_record_4ba570, clear_contents_4ba430, 7)
CLEAR_RECORD(clear_record_4ba740, clear_contents_4ba430, 7)
CLEAR_RECORD(clear_record_4ba790, clear_contents_4ba430, 7)
CLEAR_RECORD(clear_record_4be140, clear_contents_4bdfb0, 7)
CLEAR_RECORD(clear_record_4be230, clear_contents_4bdfb0, 7)
CLEAR_RECORD(clear_record_4be2d0, clear_contents_4bdfb0, 7)
CLEAR_RECORD(clear_record_4c2ab0, clear_contents_4c28b0, 7)
CLEAR_RECORD(clear_record_4c2d20, clear_contents_4c28b0, 7)
CLEAR_RECORD(clear_record_4c2dd0, clear_contents_4c28b0, 7)
CLEAR_RECORD(clear_record_4c5b50, clear_contents_4c57c0, 7)
CLEAR_RECORD(clear_record_4c5c70, clear_contents_4c57c0, 7)
CLEAR_RECORD(clear_record_4c5cf0, clear_contents_4c57c0, 7)
CLEAR_RECORD(clear_record_4c6df0, clear_contents_4c6c60, 7)
CLEAR_RECORD(clear_record_4c6ed0, clear_contents_4c6c60, 7)
CLEAR_RECORD(clear_record_4c6f70, clear_contents_4c6c60, 7)
CLEAR_RECORD(clear_record_4cd310, clear_contents_4cd100, 7)
CLEAR_RECORD(clear_record_4cd430, clear_contents_4cd100, 7)
CLEAR_RECORD(clear_record_4cd4b0, clear_contents_4cd100, 7)
CLEAR_RECORD(clear_record_4cf6c0, clear_contents_4cf350, 7)
CLEAR_RECORD(clear_record_4cfa10, clear_contents_4cf350, 7)
CLEAR_RECORD(clear_record_4cfc70, clear_contents_4cf350, 7)
CLEAR_RECORD(clear_record_4d60d0, clear_contents_4d5e50, 7)
CLEAR_RECORD(clear_record_4d6210, clear_contents_4d5e50, 7)
CLEAR_RECORD(clear_record_4d62a0, clear_contents_4d5e50, 7)
CLEAR_RECORD(clear_record_4db850, clear_contents_4db7e0, 7)
CLEAR_RECORD(clear_record_4db890, clear_contents_4db7e0, 7)
CLEAR_RECORD(clear_record_4db8e0, clear_contents_4db7e0, 7)
CLEAR_RECORD(clear_record_4dd370, clear_contents_4dd0d0, 7)
CLEAR_RECORD(clear_record_4dd3c0, clear_contents_4dd0d0, 7)
CLEAR_RECORD(clear_record_4dd4a0, clear_contents_4dd0d0, 7)
CLEAR_RECORD(clear_record_4de4e0, clear_contents_4de400, 7)
CLEAR_RECORD(clear_record_4de540, clear_contents_4de400, 7)
CLEAR_RECORD(clear_record_4de590, clear_contents_4de400, 7)
CLEAR_RECORD(clear_record_4e3250, clear_contents_4e2f60, 7)
CLEAR_RECORD(clear_record_4e32a0, clear_contents_4e3040, 7)
CLEAR_RECORD(clear_record_4e3310, clear_contents_4e2f60, 7)
CLEAR_RECORD(clear_record_4e33a0, clear_contents_4e3040, 7)
CLEAR_RECORD(clear_record_4e3450, clear_contents_4e2f60, 7)
CLEAR_RECORD(clear_record_4e35b0, clear_contents_4e3040, 7)
CLEAR_RECORD(clear_record_4e4bc0, clear_contents_4e4a30, 7)
CLEAR_RECORD(clear_record_4e4c20, clear_contents_4e4a30, 7)
CLEAR_RECORD(clear_record_4e4dd0, clear_contents_4e4a30, 7)
CLEAR_RECORD(clear_record_4e6ce0, clear_contents_4e6ae0, 7)
CLEAR_RECORD(clear_record_4e6e10, clear_contents_4e6ae0, 7)
CLEAR_RECORD(clear_record_4e6e90, clear_contents_4e6ae0, 7)
CLEAR_RECORD(clear_record_4f7d30, clear_contents_4f7900, 7)
CLEAR_RECORD(clear_record_4f7e40, clear_contents_4f7900, 7)
CLEAR_RECORD(clear_record_4f7f40, clear_contents_4f7900, 7)
CLEAR_RECORD(clear_record_73aca0, clear_contents_73ac60, 7)
CLEAR_RECORD(clear_record_73ad60, clear_contents_73ac60, 7)
CLEAR_RECORD(clear_record_73adf0, clear_contents_73ac60, 7)
