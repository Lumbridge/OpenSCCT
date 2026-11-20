/* Native UClass placement callbacks. The registry supplies allocated storage. */
struct object_header {
    const void *methods;
};

struct material_header {
    struct object_header object;
    unsigned char fields_before_28[0x28 - sizeof(struct object_header)];
    unsigned int field_28;
    float field_2c;
    unsigned char fields_before_flags[0x44 - 0x30];
    unsigned int flags;
};

#define PLACE_OBJECT(type) \
    extern const void *const vtable_##type[]; \
    void place_##type(struct object_header *storage) \
    { \
        if (storage) \
            storage->methods = vtable_##type; \
    }

#define PLACE_MATERIAL(type) \
    extern const void *const vtable_##type[]; \
    void place_##type(struct material_header *storage) \
    { \
        if (storage) { \
            storage->flags &= ~12u; \
            storage->field_28 = 0; \
            storage->field_2c = 1.0f; \
            storage->object.methods = vtable_##type; \
        } \
    }

PLACE_OBJECT(UAnimNotify)
PLACE_OBJECT(UAnimNotify_DestroyEffect)
PLACE_OBJECT(UAnimNotify_Effect)
PLACE_OBJECT(UAnimNotify_Script)
PLACE_OBJECT(UAnimNotify_Scripted)
PLACE_OBJECT(UAnimNotify_Sound)
PLACE_OBJECT(UAnimNotify_StopSound)
PLACE_OBJECT(UAnimation)
PLACE_OBJECT(UCanvas)
PLACE_OBJECT(UCheatManager)
PLACE_OBJECT(UGUI)
PLACE_OBJECT(UGUIButton)
PLACE_OBJECT(UGUICadre)
PLACE_OBJECT(UGUIChatBox)
PLACE_OBJECT(UGUICheckBox)
PLACE_OBJECT(UGUICheckBoxButton)
PLACE_OBJECT(UGUICircularList)
PLACE_OBJECT(UGUIComboBox)
PLACE_OBJECT(UGUIComponent)
PLACE_OBJECT(UGUIEditBox)
PLACE_OBJECT(UGUIFont)
PLACE_OBJECT(UGUIGFXButton)
PLACE_OBJECT(UGUIHorzGripButton)
PLACE_OBJECT(UGUIHorzList)
PLACE_OBJECT(UGUIHorzScrollBar)
PLACE_OBJECT(UGUIHorzScrollButton)
PLACE_OBJECT(UGUIHorzScrollZone)
PLACE_OBJECT(UGUIImage)
PLACE_OBJECT(UGUILabel)
PLACE_OBJECT(UGUILabelPad)
PLACE_OBJECT(UGUIList)
PLACE_OBJECT(UGUIListBase)
PLACE_OBJECT(UGUIListBox)
PLACE_OBJECT(UGUIListBoxBase)
PLACE_OBJECT(UGUIListFriends)
PLACE_OBJECT(UGUIMOTDInfoBox)
PLACE_OBJECT(UGUIMenuOption)
PLACE_OBJECT(UGUIMultiColumnListBox)
PLACE_OBJECT(UGUIMultiColumnListHeader)
PLACE_OBJECT(UGUIMultiComponent)
PLACE_OBJECT(UGUIMultiPageLabel)
PLACE_OBJECT(UGUIMultiPageLabelBox)
PLACE_OBJECT(UGUINumericEdit)
PLACE_OBJECT(UGUIPage)
PLACE_OBJECT(UGUIPageWaitLaunch)
PLACE_OBJECT(UGUIPanel)
PLACE_OBJECT(UGUIProgressBar)
PLACE_OBJECT(UGUIScrollBarBase)
PLACE_OBJECT(UGUIScrollText)
PLACE_OBJECT(UGUIScrollTextBox)
PLACE_OBJECT(UGUISearchFilterBox)
PLACE_OBJECT(UGUISessionInfoBox)
PLACE_OBJECT(UGUISlider)
PLACE_OBJECT(UGUISpinnerButton)
PLACE_OBJECT(UGUISplitter)
PLACE_OBJECT(UGUITabButton)
PLACE_OBJECT(UGUITabControl)
PLACE_OBJECT(UGUITabPanel)
PLACE_OBJECT(UGUITitleBar)
PLACE_OBJECT(UGUIVertGripButton)
PLACE_OBJECT(UGUIVertList)
PLACE_OBJECT(UGUIVertScrollBar)
PLACE_OBJECT(UGUIVertScrollBarLabel)
PLACE_OBJECT(UGUIVertScrollButton)
PLACE_OBJECT(UGUIVertScrollZone)
PLACE_OBJECT(UI3DL2Listener)
PLACE_OBJECT(UInteraction)
PLACE_OBJECT(UInteractionMaster)
PLACE_OBJECT(UInteractions)
PLACE_OBJECT(ULANErrorHandler)
PLACE_OBJECT(ULevelSummary)
PLACE_OBJECT(UMagmaInteraction)
PLACE_OBJECT(UMatchInfo)
PLACE_OBJECT(UMeshObject)
PLACE_OBJECT(UObject)
PLACE_OBJECT(UObjectExporterT3D)
PLACE_OBJECT(UPersistentData)
PLACE_OBJECT(UPlayerInput)
PLACE_OBJECT(USMapSettings)
PLACE_OBJECT(USMatchInfo)
PLACE_OBJECT(USPlayerProfile)
PLACE_OBJECT(USXBoxLiveRef)
PLACE_OBJECT(UStrategicMap)
PLACE_OBJECT(UmoEditBox)
PLACE_OBJECT(UmoImageListMap)
PLACE_OBJECT(UmoListEditBox)

PLACE_MATERIAL(UColorModifier)
PLACE_MATERIAL(UCombiner)
PLACE_MATERIAL(UConstantColor)
PLACE_MATERIAL(UConstantMaterial)
PLACE_MATERIAL(UEMFChannelTextures)
PLACE_MATERIAL(UEMFTextureModifier)
PLACE_MATERIAL(UEchelonLightingMaterial)
PLACE_MATERIAL(UFadeColor)
PLACE_MATERIAL(UFinalBlend)
PLACE_MATERIAL(UHeatTextureModifier)
PLACE_MATERIAL(ULitSpecularMaterial)
PLACE_MATERIAL(UMaterialSequence)
PLACE_MATERIAL(UMaterialSwitch)
PLACE_MATERIAL(UModifier)
PLACE_MATERIAL(UOpacityModifier)
PLACE_MATERIAL(UParticleMaterial)
PLACE_MATERIAL(UProjectorMaterial)
PLACE_MATERIAL(UProxyBitmapMaterial)
PLACE_MATERIAL(UScriptedTexture)
PLACE_MATERIAL(UShader)
PLACE_MATERIAL(UShadowBitmapMaterial)
PLACE_MATERIAL(USpecialVisionTextureModifier)
PLACE_MATERIAL(UTexCoordMaterial)
PLACE_MATERIAL(UTexCoordSource)
PLACE_MATERIAL(UTexEnvMap)
PLACE_MATERIAL(UTexFriendlyPanner)
PLACE_MATERIAL(UTexMatrix)
PLACE_MATERIAL(UTexModifier)
PLACE_MATERIAL(UTexOscillator)
PLACE_MATERIAL(UTexOscillatorTriggered)
PLACE_MATERIAL(UTexPanner)
PLACE_MATERIAL(UTexPannerTriggered)
PLACE_MATERIAL(UTexRotator)
PLACE_MATERIAL(UTexScaler)
PLACE_MATERIAL(UVertexColor)
