#include "campaign_primitive_cast.h"

#define REGISTER_CAMPAIGN_CAST(name, index)                      \
    extern const CampaignNative callback_##name;                    \
    extern unsigned char cast_result_##name;                        \
    void register_cast_##name(void)                                 \
    {                                                               \
        cast_result_##name = campaign_assign_cast(index, &callback_##name); \
    }

REGISTER_CAMPAIGN_CAST(UObject_ByteToInt, 58)
REGISTER_CAMPAIGN_CAST(UObject_ByteToBool, 59)
REGISTER_CAMPAIGN_CAST(UObject_ByteToFloat, 60)
REGISTER_CAMPAIGN_CAST(UObject_ByteToString, 82)
REGISTER_CAMPAIGN_CAST(UObject_IntToByte, 61)
REGISTER_CAMPAIGN_CAST(UObject_IntToBool, 62)
REGISTER_CAMPAIGN_CAST(UObject_IntToFloat, 63)
REGISTER_CAMPAIGN_CAST(UObject_IntToString, 83)
REGISTER_CAMPAIGN_CAST(UObject_BoolToByte, 64)
REGISTER_CAMPAIGN_CAST(UObject_BoolToInt, 65)
REGISTER_CAMPAIGN_CAST(UObject_BoolToFloat, 66)
REGISTER_CAMPAIGN_CAST(UObject_BoolToString, 84)
REGISTER_CAMPAIGN_CAST(UObject_FloatToByte, 67)
REGISTER_CAMPAIGN_CAST(UObject_FloatToInt, 68)
REGISTER_CAMPAIGN_CAST(UObject_FloatToBool, 69)
REGISTER_CAMPAIGN_CAST(UObject_FloatToString, 85)
REGISTER_CAMPAIGN_CAST(UObject_ObjectToBool, 71)
REGISTER_CAMPAIGN_CAST(UObject_ObjectToString, 86)
REGISTER_CAMPAIGN_CAST(UObject_NameToBool, 72)
REGISTER_CAMPAIGN_CAST(UObject_NameToString, 87)
REGISTER_CAMPAIGN_CAST(UObject_StringToByte, 73)
REGISTER_CAMPAIGN_CAST(UObject_StringToInt, 74)
REGISTER_CAMPAIGN_CAST(UObject_StringToBool, 75)
REGISTER_CAMPAIGN_CAST(UObject_StringToFloat, 76)
REGISTER_CAMPAIGN_CAST(UObject_StringToVector, 77)
REGISTER_CAMPAIGN_CAST(UObject_StringToRotator, 78)
REGISTER_CAMPAIGN_CAST(UObject_VectorToBool, 79)
REGISTER_CAMPAIGN_CAST(UObject_VectorToString, 88)
REGISTER_CAMPAIGN_CAST(UObject_VectorToRotator, 80)
REGISTER_CAMPAIGN_CAST(UObject_RotatorToBool, 81)
REGISTER_CAMPAIGN_CAST(UObject_RotatorToVector, 57)
REGISTER_CAMPAIGN_CAST(UObject_RotatorToString, 89)
