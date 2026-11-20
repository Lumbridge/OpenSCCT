struct PoolRecord { const void *methods; unsigned short allocation_id; };
struct DeleteFlags { unsigned value; };
struct Pool;
struct Operations {
    void *reserved[5];
    void (__fastcall *release)(struct Pool *, unsigned scratch_edx, struct PoolRecord *,
                               unsigned allocation_id, unsigned kind);
};
struct Pool { const struct Operations *ops; };
extern struct Pool *pool;

#define DELETE_POOL_RECORD(Entry, Destroy, Kind) \
    struct PoolRecord *__fastcall Entry(struct PoolRecord *self, struct DeleteFlags flags) \
    { \
        Destroy(self); \
        if (flags.value & 1) { \
            unsigned id = self->allocation_id; \
            pool->ops->release(pool, id, self, id, Kind); \
        } \
        return self; \
    }

extern void __fastcall destroy_08a4d0(struct PoolRecord *self);
extern void __fastcall destroy_08a660(struct PoolRecord *self);
extern void __fastcall destroy_0a1f80(struct PoolRecord *self);
extern void __fastcall destroy_0a2270(struct PoolRecord *self);
extern void __fastcall destroy_1d2e80(struct PoolRecord *self);
extern void __fastcall destroy_1d2eb0(struct PoolRecord *self);
extern void __fastcall destroy_4fc300(struct PoolRecord *self);
extern void __fastcall destroy_4ffa80(struct PoolRecord *self);
extern void __fastcall destroy_502090(struct PoolRecord *self);
extern void __fastcall destroy_5027f0(struct PoolRecord *self);
extern void __fastcall destroy_5036d0(struct PoolRecord *self);
extern void __fastcall destroy_5052b0(struct PoolRecord *self);
extern void __fastcall destroy_5055b0(struct PoolRecord *self);
extern void __fastcall destroy_505fa0(struct PoolRecord *self);
extern void __fastcall destroy_5061d0(struct PoolRecord *self);
extern void __fastcall destroy_5064e0(struct PoolRecord *self);
extern void __fastcall destroy_506610(struct PoolRecord *self);
extern void __fastcall destroy_5071a0(struct PoolRecord *self);
extern void __fastcall destroy_507710(struct PoolRecord *self);
extern void __fastcall destroy_5091e0(struct PoolRecord *self);
extern void __fastcall destroy_50b950(struct PoolRecord *self);
extern void __fastcall destroy_50de40(struct PoolRecord *self);
extern void __fastcall destroy_513250(struct PoolRecord *self);
extern void __fastcall destroy_5146a0(struct PoolRecord *self);
extern void __fastcall destroy_5160c0(struct PoolRecord *self);
extern void __fastcall destroy_5183f0(struct PoolRecord *self);
extern void __fastcall destroy_5313a0(struct PoolRecord *self);
extern void __fastcall destroy_531f90(struct PoolRecord *self);
extern void __fastcall destroy_534230(struct PoolRecord *self);
extern void __fastcall destroy_5349b0(struct PoolRecord *self);
extern void __fastcall destroy_535e10(struct PoolRecord *self);
extern void __fastcall destroy_538cc0(struct PoolRecord *self);
extern void __fastcall destroy_539d40(struct PoolRecord *self);
extern void __fastcall destroy_5504c0(struct PoolRecord *self);
extern void __fastcall destroy_550680(struct PoolRecord *self);
extern void __fastcall destroy_559490(struct PoolRecord *self);
extern void __fastcall destroy_55e210(struct PoolRecord *self);
extern void __fastcall destroy_55e400(struct PoolRecord *self);
extern void __fastcall destroy_565370(struct PoolRecord *self);
extern void __fastcall destroy_567fa0(struct PoolRecord *self);
extern void __fastcall destroy_568290(struct PoolRecord *self);
extern void __fastcall destroy_5685e0(struct PoolRecord *self);
extern void __fastcall destroy_56d070(struct PoolRecord *self);
extern void __fastcall destroy_571680(struct PoolRecord *self);
extern void __fastcall destroy_579240(struct PoolRecord *self);
extern void __fastcall destroy_57a070(struct PoolRecord *self);
extern void __fastcall destroy_57b410(struct PoolRecord *self);
extern void __fastcall destroy_57c580(struct PoolRecord *self);
extern void __fastcall destroy_57d320(struct PoolRecord *self);
extern void __fastcall destroy_57eeb0(struct PoolRecord *self);
extern void __fastcall destroy_57f3c0(struct PoolRecord *self);
extern void __fastcall destroy_57f690(struct PoolRecord *self);
extern void __fastcall destroy_5801b0(struct PoolRecord *self);
extern void __fastcall destroy_583b40(struct PoolRecord *self);
extern void __fastcall destroy_584160(struct PoolRecord *self);
extern void __fastcall destroy_5847f0(struct PoolRecord *self);
extern void __fastcall destroy_584ad0(struct PoolRecord *self);
extern void __fastcall destroy_584f90(struct PoolRecord *self);
extern void __fastcall destroy_586bf0(struct PoolRecord *self);
extern void __fastcall destroy_589020(struct PoolRecord *self);
extern void __fastcall destroy_5897f0(struct PoolRecord *self);
extern void __fastcall destroy_58ae80(struct PoolRecord *self);
extern void __fastcall destroy_59b950(struct PoolRecord *self);
extern void __fastcall destroy_59bdd0(struct PoolRecord *self);
extern void __fastcall destroy_59c050(struct PoolRecord *self);
extern void __fastcall destroy_59e350(struct PoolRecord *self);
extern void __fastcall destroy_59f270(struct PoolRecord *self);
extern void __fastcall destroy_59f3d0(struct PoolRecord *self);
extern void __fastcall destroy_59f670(struct PoolRecord *self);
extern void __fastcall destroy_5a0110(struct PoolRecord *self);
extern void __fastcall destroy_5a02b0(struct PoolRecord *self);
extern void __fastcall destroy_5a09a0(struct PoolRecord *self);
extern void __fastcall destroy_5a0b10(struct PoolRecord *self);
extern void __fastcall destroy_5a0d60(struct PoolRecord *self);
extern void __fastcall destroy_5a1340(struct PoolRecord *self);
extern void __fastcall destroy_5a1420(struct PoolRecord *self);
extern void __fastcall destroy_5a1600(struct PoolRecord *self);
extern void __fastcall destroy_5a19b0(struct PoolRecord *self);
extern void __fastcall destroy_5a1fb0(struct PoolRecord *self);
extern void __fastcall destroy_5a3660(struct PoolRecord *self);
extern void __fastcall destroy_6fc8b0(struct PoolRecord *self);
extern void __fastcall destroy_6fe0a0(struct PoolRecord *self);
extern void __fastcall destroy_700800(struct PoolRecord *self);
extern void __fastcall destroy_700a00(struct PoolRecord *self);
extern void __fastcall destroy_700a70(struct PoolRecord *self);
extern void __fastcall destroy_718db0(struct PoolRecord *self);
extern void __fastcall destroy_71bfb0(struct PoolRecord *self);
extern void __fastcall destroy_71cfb0(struct PoolRecord *self);
extern void __fastcall destroy_71e2c0(struct PoolRecord *self);
extern void __fastcall destroy_791d60(struct PoolRecord *self);
extern void __fastcall destroy_791db0(struct PoolRecord *self);
extern void __fastcall destroy_792de0(struct PoolRecord *self);

DELETE_POOL_RECORD(delete_pool_08b130, destroy_08a660, 34)
DELETE_POOL_RECORD(delete_pool_08c670, destroy_5071a0, 34)
DELETE_POOL_RECORD(delete_pool_0a22e0, destroy_0a2270, 39)
DELETE_POOL_RECORD(delete_pool_0a3d30, destroy_55e210, 36)
DELETE_POOL_RECORD(delete_pool_1bc900, destroy_08a660, 34)
DELETE_POOL_RECORD(delete_pool_4fc2d0, destroy_4fc300, 19)
DELETE_POOL_RECORD(delete_pool_4ffb60, destroy_4ffa80, 25)
DELETE_POOL_RECORD(delete_pool_502060, destroy_502090, 21)
DELETE_POOL_RECORD(delete_pool_502bb0, destroy_5027f0, 16)
DELETE_POOL_RECORD(delete_pool_5047e0, destroy_5064e0, 6)
DELETE_POOL_RECORD(delete_pool_505310, destroy_5052b0, 6)
DELETE_POOL_RECORD(delete_pool_5056e0, destroy_5055b0, 6)
DELETE_POOL_RECORD(delete_pool_506020, destroy_505fa0, 6)
DELETE_POOL_RECORD(delete_pool_506260, destroy_5061d0, 5)
DELETE_POOL_RECORD(delete_pool_506690, destroy_506610, 6)
DELETE_POOL_RECORD(delete_pool_506cb0, destroy_08a660, 34)
DELETE_POOL_RECORD(delete_pool_507900, destroy_507710, 34)
DELETE_POOL_RECORD(delete_pool_5086d0, destroy_1d2eb0, 34)
DELETE_POOL_RECORD(delete_pool_5091b0, destroy_5091e0, 34)
DELETE_POOL_RECORD(delete_pool_509eb0, destroy_1d2eb0, 34)
DELETE_POOL_RECORD(delete_pool_50b920, destroy_50b950, 34)
DELETE_POOL_RECORD(delete_pool_50c3f0, destroy_1d2eb0, 34)
DELETE_POOL_RECORD(delete_pool_50cd00, destroy_1d2eb0, 34)
DELETE_POOL_RECORD(delete_pool_50dec0, destroy_50de40, 34)
DELETE_POOL_RECORD(delete_pool_513e50, destroy_513250, 26)
DELETE_POOL_RECORD(delete_pool_514c40, destroy_5146a0, 26)
DELETE_POOL_RECORD(delete_pool_516760, destroy_5160c0, 26)
DELETE_POOL_RECORD(delete_pool_518700, destroy_5183f0, 26)
DELETE_POOL_RECORD(delete_pool_5316c0, destroy_5313a0, 26)
DELETE_POOL_RECORD(delete_pool_532320, destroy_531f90, 26)
DELETE_POOL_RECORD(delete_pool_5345c0, destroy_534230, 26)
DELETE_POOL_RECORD(delete_pool_534e20, destroy_5349b0, 26)
DELETE_POOL_RECORD(delete_pool_537db0, destroy_535e10, 26)
DELETE_POOL_RECORD(delete_pool_538d60, destroy_538cc0, 34)
DELETE_POOL_RECORD(delete_pool_53a0c0, destroy_539d40, 26)
DELETE_POOL_RECORD(delete_pool_5505b0, destroy_5504c0, 16)
DELETE_POOL_RECORD(delete_pool_550770, destroy_550680, 40)
DELETE_POOL_RECORD(delete_pool_554b60, destroy_08a660, 34)
DELETE_POOL_RECORD(delete_pool_5599c0, destroy_559490, 42)
DELETE_POOL_RECORD(delete_pool_55b4c0, destroy_56d070, 39)
DELETE_POOL_RECORD(delete_pool_55bb20, destroy_56d070, 39)
DELETE_POOL_RECORD(delete_pool_55bce0, destroy_56d070, 39)
DELETE_POOL_RECORD(delete_pool_55c150, destroy_0a1f80, 39)
DELETE_POOL_RECORD(delete_pool_55c270, destroy_0a1f80, 39)
DELETE_POOL_RECORD(delete_pool_55c420, destroy_0a1f80, 39)
DELETE_POOL_RECORD(delete_pool_55c560, destroy_0a1f80, 39)
DELETE_POOL_RECORD(delete_pool_55d650, destroy_56d070, 39)
DELETE_POOL_RECORD(delete_pool_55df80, destroy_56d070, 39)
DELETE_POOL_RECORD(delete_pool_55e380, destroy_55e210, 36)
DELETE_POOL_RECORD(delete_pool_55e5a0, destroy_55e400, 39)
DELETE_POOL_RECORD(delete_pool_55f940, destroy_56d070, 39)
DELETE_POOL_RECORD(delete_pool_560610, destroy_56d070, 39)
DELETE_POOL_RECORD(delete_pool_561fb0, destroy_56d070, 39)
DELETE_POOL_RECORD(delete_pool_562b70, destroy_56d070, 39)
DELETE_POOL_RECORD(delete_pool_5665e0, destroy_565370, 45)
DELETE_POOL_RECORD(delete_pool_56aa80, destroy_5685e0, 41)
DELETE_POOL_RECORD(delete_pool_56cec0, destroy_568290, 38)
DELETE_POOL_RECORD(delete_pool_56e490, destroy_567fa0, 39)
DELETE_POOL_RECORD(delete_pool_570360, destroy_5036d0, 11)
DELETE_POOL_RECORD(delete_pool_571820, destroy_571680, 34)
DELETE_POOL_RECORD(delete_pool_578580, destroy_5036d0, 6)
DELETE_POOL_RECORD(delete_pool_578c20, destroy_57b410, 11)
DELETE_POOL_RECORD(delete_pool_579210, destroy_579240, 11)
DELETE_POOL_RECORD(delete_pool_579480, destroy_08a4d0, 5)
DELETE_POOL_RECORD(delete_pool_57a040, destroy_57a070, 19)
DELETE_POOL_RECORD(delete_pool_57a8e0, destroy_718db0, 36)
DELETE_POOL_RECORD(delete_pool_57c0d0, destroy_08a660, 34)
DELETE_POOL_RECORD(delete_pool_57c660, destroy_57c580, 34)
DELETE_POOL_RECORD(delete_pool_57d470, destroy_57d320, 5)
DELETE_POOL_RECORD(delete_pool_57ee80, destroy_57eeb0, 19)
DELETE_POOL_RECORD(delete_pool_57f390, destroy_57f3c0, 5)
DELETE_POOL_RECORD(delete_pool_57f720, destroy_57f690, 5)
DELETE_POOL_RECORD(delete_pool_5802f0, destroy_5801b0, 5)
DELETE_POOL_RECORD(delete_pool_581160, destroy_584f90, 5)
DELETE_POOL_RECORD(delete_pool_583b10, destroy_583b40, 5)
DELETE_POOL_RECORD(delete_pool_584230, destroy_584160, 5)
DELETE_POOL_RECORD(delete_pool_5848c0, destroy_5847f0, 5)
DELETE_POOL_RECORD(delete_pool_584c20, destroy_584ad0, 5)
DELETE_POOL_RECORD(delete_pool_586cb0, destroy_586bf0, 5)
DELETE_POOL_RECORD(delete_pool_588ff0, destroy_589020, 5)
DELETE_POOL_RECORD(delete_pool_5897c0, destroy_5897f0, 5)
DELETE_POOL_RECORD(delete_pool_58af50, destroy_58ae80, 5)
DELETE_POOL_RECORD(delete_pool_59b920, destroy_59b950, 10)
DELETE_POOL_RECORD(delete_pool_59be90, destroy_59bdd0, 10)
DELETE_POOL_RECORD(delete_pool_59c250, destroy_59c050, 10)
DELETE_POOL_RECORD(delete_pool_59e420, destroy_59e350, 10)
DELETE_POOL_RECORD(delete_pool_59f3a0, destroy_59f3d0, 10)
DELETE_POOL_RECORD(delete_pool_59f740, destroy_59f670, 10)
DELETE_POOL_RECORD(delete_pool_5a0280, destroy_5a02b0, 10)
DELETE_POOL_RECORD(delete_pool_5a0ae0, destroy_5a0b10, 10)
DELETE_POOL_RECORD(delete_pool_5a0ca0, destroy_59bdd0, 10)
DELETE_POOL_RECORD(delete_pool_5a1070, destroy_5a0d60, 10)
DELETE_POOL_RECORD(delete_pool_5a13f0, destroy_5a1420, 10)
DELETE_POOL_RECORD(delete_pool_5a1780, destroy_5a1600, 10)
DELETE_POOL_RECORD(delete_pool_5a1a00, destroy_5a19b0, 10)
DELETE_POOL_RECORD(delete_pool_5a24c0, destroy_5a1fb0, 10)
DELETE_POOL_RECORD(delete_pool_5a3800, destroy_5a3660, 10)
DELETE_POOL_RECORD(delete_pool_5a4060, destroy_59f270, 10)
DELETE_POOL_RECORD(delete_pool_5a4130, destroy_59bdd0, 10)
DELETE_POOL_RECORD(delete_pool_5a4300, destroy_5a0110, 10)
DELETE_POOL_RECORD(delete_pool_5a4430, destroy_5a09a0, 10)
DELETE_POOL_RECORD(delete_pool_5a4570, destroy_5a1340, 10)
DELETE_POOL_RECORD(delete_pool_5a5770, destroy_5a1600, 10)
DELETE_POOL_RECORD(delete_pool_5a5ac0, destroy_08a4d0, 10)
DELETE_POOL_RECORD(delete_pool_6eeaa0, destroy_5036d0, 6)
DELETE_POOL_RECORD(delete_pool_6fd6c0, destroy_6fc8b0, 28)
DELETE_POOL_RECORD(delete_pool_6fe070, destroy_6fe0a0, 34)
DELETE_POOL_RECORD(delete_pool_6ff560, destroy_1d2e80, 34)
DELETE_POOL_RECORD(delete_pool_7007d0, destroy_700800, 34)
DELETE_POOL_RECORD(delete_pool_7009d0, destroy_700a00, 34)
DELETE_POOL_RECORD(delete_pool_700df0, destroy_700a70, 34)
DELETE_POOL_RECORD(delete_pool_718ec0, destroy_718db0, 36)
DELETE_POOL_RECORD(delete_pool_71c0c0, destroy_71bfb0, 39)
DELETE_POOL_RECORD(delete_pool_71ca70, destroy_71cfb0, 44)
DELETE_POOL_RECORD(delete_pool_71e290, destroy_71e2c0, 39)
DELETE_POOL_RECORD(delete_pool_791d30, destroy_791d60, 1)
DELETE_POOL_RECORD(delete_pool_791d80, destroy_791db0, 1)
DELETE_POOL_RECORD(delete_pool_792db0, destroy_792de0, 1)
