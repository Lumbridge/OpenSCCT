// Unreal Engine 2 class destructors from the Steam Versus payload.
// Each class destructor runs UObject::ConditionalDestroy() before its parent's
// destructor, as the engine's DECLARE_CLASS destructor does. Only the members these
// destructors touch are declared; VirtualSlot names stand for engine virtual methods
// whose names this translation unit does not need.
enum { INDEX_NONE = -1 };
enum EObjectFlags { RF_Destroyed = 0x00800000, RF_DebugDestroy = 0x80000000 };

class UObject {
public:
    virtual ~UObject();
    virtual void VirtualSlot1();
    virtual void VirtualSlot2();
    virtual void VirtualSlot3();
    virtual void VirtualSlot4();
    virtual void VirtualSlot5();
    virtual void VirtualSlot6();
    virtual void VirtualSlot7();
    virtual void VirtualSlot8();
    virtual void Destroy();
    int Index;
    unsigned Reserved[5];
    unsigned ObjectFlags;
    int ConditionalDestroy()
    {
        if (Index != INDEX_NONE && !(ObjectFlags & RF_Destroyed)) {
            ObjectFlags = (ObjectFlags & ~(RF_DebugDestroy | RF_Destroyed)) | RF_Destroyed;
            Destroy();
            return 1;
        }
        return 0;
    }
};

// Parent classes whose own destructors are matched elsewhere or remain open.
class AActor : public UObject { public: virtual ~AActor(); };
class AESoftBodyActor : public UObject { public: virtual ~AESoftBodyActor(); };
class AEmitter : public UObject { public: virtual ~AEmitter(); };
class AInventory : public UObject { public: virtual ~AInventory(); };
class AMotionTrackingVision : public UObject { public: virtual ~AMotionTrackingVision(); };
class AMover : public UObject { public: virtual ~AMover(); };
class ASBasePawn : public UObject { public: virtual ~ASBasePawn(); };
class ASBasicGamePlayObject : public UObject { public: virtual ~ASBasicGamePlayObject(); };
class ASGamePlayObject : public UObject { public: virtual ~ASGamePlayObject(); };
class ASInteractObject : public UObject { public: virtual ~ASInteractObject(); };
class ASObjectiveTrigger : public UObject { public: virtual ~ASObjectiveTrigger(); };
class ASWeaponDefense : public UObject { public: virtual ~ASWeaponDefense(); };
class AVolume : public UObject { public: virtual ~AVolume(); };
class AZoneInfo : public UObject { public: virtual ~AZoneInfo(); };
class UConstantMaterial : public UObject { public: virtual ~UConstantMaterial(); };
class UGUIButton : public UObject { public: virtual ~UGUIButton(); };
class UGUIComponent : public UObject { public: virtual ~UGUIComponent(); };
class UGUILabel : public UObject { public: virtual ~UGUILabel(); };
class UGUIList : public UObject { public: virtual ~UGUIList(); };
class UGUIMenuOption : public UObject { public: virtual ~UGUIMenuOption(); };
class UGUIMultiComponent : public UObject { public: virtual ~UGUIMultiComponent(); };
class UGUIPageWaitLaunch : public UObject { public: virtual ~UGUIPageWaitLaunch(); };
class UMatchInfo : public UObject { public: virtual ~UMatchInfo(); };
class UMeshInstance : public UObject { public: virtual ~UMeshInstance(); };
class UModifier : public UObject { public: virtual ~UModifier(); };
class UObjectProperty : public UObject { public: virtual ~UObjectProperty(); };
class UParticleEmitter : public UObject { public: virtual ~UParticleEmitter(); };
class UStruct : public UObject { public: virtual ~UStruct(); };

class UState : public UStruct { public: virtual ~UState(); };
UState::~UState() { ConditionalDestroy(); }
class UPackage : public UObject { public: virtual ~UPackage(); };
UPackage::~UPackage() { ConditionalDestroy(); }
class UFunction : public UStruct { public: virtual ~UFunction(); };
UFunction::~UFunction() { ConditionalDestroy(); }
class UClassProperty : public UObjectProperty { public: virtual ~UClassProperty(); };
UClassProperty::~UClassProperty() { ConditionalDestroy(); }
class USpriteEmitter : public UParticleEmitter { public: virtual ~USpriteEmitter(); };
USpriteEmitter::~USpriteEmitter() { ConditionalDestroy(); }
class UFlicFlocEmitter : public USpriteEmitter { public: virtual ~UFlicFlocEmitter(); };
UFlicFlocEmitter::~UFlicFlocEmitter() { ConditionalDestroy(); }
class UInteractions : public UObject { public: virtual ~UInteractions(); };
UInteractions::~UInteractions() { ConditionalDestroy(); }
class UInteraction : public UInteractions { public: virtual ~UInteraction(); };
UInteraction::~UInteraction() { ConditionalDestroy(); }
class UMagmaInteraction : public UInteraction { public: virtual ~UMagmaInteraction(); };
UMagmaInteraction::~UMagmaInteraction() { ConditionalDestroy(); }
class ULANErrorHandler : public UObject { public: virtual ~ULANErrorHandler(); };
ULANErrorHandler::~ULANErrorHandler() { ConditionalDestroy(); }
class ADecoVolumeObject : public AActor { public: virtual ~ADecoVolumeObject(); };
ADecoVolumeObject::~ADecoVolumeObject() { ConditionalDestroy(); }
class AKeypoint : public AActor { public: virtual ~AKeypoint(); };
AKeypoint::~AKeypoint() { ConditionalDestroy(); }
class ULodMeshInstance : public UMeshInstance { public: virtual ~ULodMeshInstance(); };
ULodMeshInstance::~ULodMeshInstance() { ConditionalDestroy(); }
class AStaticMeshActor : public AActor { public: virtual ~AStaticMeshActor(); };
AStaticMeshActor::~AStaticMeshActor() { ConditionalDestroy(); }
class UAnimNotify : public UObject { public: virtual ~UAnimNotify(); };
UAnimNotify::~UAnimNotify() { ConditionalDestroy(); }
class UAnimNotify_Scripted : public UAnimNotify { public: virtual ~UAnimNotify_Scripted(); };
UAnimNotify_Scripted::~UAnimNotify_Scripted() { ConditionalDestroy(); }
class UAnimNotify_Script : public UAnimNotify { public: virtual ~UAnimNotify_Script(); };
UAnimNotify_Script::~UAnimNotify_Script() { ConditionalDestroy(); }
class UAnimNotify_StopSound : public UAnimNotify { public: virtual ~UAnimNotify_StopSound(); };
UAnimNotify_StopSound::~UAnimNotify_StopSound() { ConditionalDestroy(); }
class UAnimNotify_Sound : public UAnimNotify { public: virtual ~UAnimNotify_Sound(); };
UAnimNotify_Sound::~UAnimNotify_Sound() { ConditionalDestroy(); }
class UAnimNotify_DestroyEffect : public UAnimNotify { public: virtual ~UAnimNotify_DestroyEffect(); };
UAnimNotify_DestroyEffect::~UAnimNotify_DestroyEffect() { ConditionalDestroy(); }
class UAnimNotify_Effect : public UAnimNotify { public: virtual ~UAnimNotify_Effect(); };
UAnimNotify_Effect::~UAnimNotify_Effect() { ConditionalDestroy(); }
class UAnimation : public UObject { public: virtual ~UAnimation(); };
UAnimation::~UAnimation() { ConditionalDestroy(); }
class AInfo : public AActor { public: virtual ~AInfo(); };
AInfo::~AInfo() { ConditionalDestroy(); }
class UMeshObject : public UObject { public: virtual ~UMeshObject(); };
UMeshObject::~UMeshObject() { ConditionalDestroy(); }
class AProjector : public AActor { public: virtual ~AProjector(); };
AProjector::~AProjector() { ConditionalDestroy(); }
class AShadowProjector : public AProjector { public: virtual ~AShadowProjector(); };
AShadowProjector::~AShadowProjector() { ConditionalDestroy(); }
class AMoverRotating : public AMover { public: virtual ~AMoverRotating(); };
AMoverRotating::~AMoverRotating() { ConditionalDestroy(); }
class UMeshEmitter : public UParticleEmitter { public: virtual ~UMeshEmitter(); };
UMeshEmitter::~UMeshEmitter() { ConditionalDestroy(); }
class UOpacityModifier : public UModifier { public: virtual ~UOpacityModifier(); };
UOpacityModifier::~UOpacityModifier() { ConditionalDestroy(); }
class USpecialVisionTextureModifier : public UModifier { public: virtual ~USpecialVisionTextureModifier(); };
USpecialVisionTextureModifier::~USpecialVisionTextureModifier() { ConditionalDestroy(); }
class UEMFTextureModifier : public UModifier { public: virtual ~UEMFTextureModifier(); };
UEMFTextureModifier::~UEMFTextureModifier() { ConditionalDestroy(); }
class UHeatTextureModifier : public UModifier { public: virtual ~UHeatTextureModifier(); };
UHeatTextureModifier::~UHeatTextureModifier() { ConditionalDestroy(); }
class UFinalBlend : public UModifier { public: virtual ~UFinalBlend(); };
UFinalBlend::~UFinalBlend() { ConditionalDestroy(); }
class UTexModifier : public UModifier { public: virtual ~UTexModifier(); };
UTexModifier::~UTexModifier() { ConditionalDestroy(); }
class UTexCoordSource : public UTexModifier { public: virtual ~UTexCoordSource(); };
UTexCoordSource::~UTexCoordSource() { ConditionalDestroy(); }
class UTexMatrix : public UTexModifier { public: virtual ~UTexMatrix(); };
UTexMatrix::~UTexMatrix() { ConditionalDestroy(); }
class UTexEnvMap : public UTexModifier { public: virtual ~UTexEnvMap(); };
UTexEnvMap::~UTexEnvMap() { ConditionalDestroy(); }
class UTexOscillator : public UTexModifier { public: virtual ~UTexOscillator(); };
UTexOscillator::~UTexOscillator() { ConditionalDestroy(); }
class UTexOscillatorTriggered : public UTexOscillator { public: virtual ~UTexOscillatorTriggered(); };
UTexOscillatorTriggered::~UTexOscillatorTriggered() { ConditionalDestroy(); }
class UTexRotator : public UTexModifier { public: virtual ~UTexRotator(); };
UTexRotator::~UTexRotator() { ConditionalDestroy(); }
class UTexScaler : public UTexModifier { public: virtual ~UTexScaler(); };
UTexScaler::~UTexScaler() { ConditionalDestroy(); }
class UTexFriendlyPanner : public UTexModifier { public: virtual ~UTexFriendlyPanner(); };
UTexFriendlyPanner::~UTexFriendlyPanner() { ConditionalDestroy(); }
class UTexPanner : public UTexModifier { public: virtual ~UTexPanner(); };
UTexPanner::~UTexPanner() { ConditionalDestroy(); }
class UTexPannerTriggered : public UTexPanner { public: virtual ~UTexPannerTriggered(); };
UTexPannerTriggered::~UTexPannerTriggered() { ConditionalDestroy(); }
class UFadeColor : public UConstantMaterial { public: virtual ~UFadeColor(); };
UFadeColor::~UFadeColor() { ConditionalDestroy(); }
class UConstantColor : public UConstantMaterial { public: virtual ~UConstantColor(); };
UConstantColor::~UConstantColor() { ConditionalDestroy(); }
class UColorModifier : public UModifier { public: virtual ~UColorModifier(); };
UColorModifier::~UColorModifier() { ConditionalDestroy(); }
class UPlayerInput : public UObject { public: virtual ~UPlayerInput(); };
UPlayerInput::~UPlayerInput() { ConditionalDestroy(); }
class UCheatManager : public UObject { public: virtual ~UCheatManager(); };
UCheatManager::~UCheatManager() { ConditionalDestroy(); }
class UCanvas : public UObject { public: virtual ~UCanvas(); };
UCanvas::~UCanvas() { ConditionalDestroy(); }
class UI3DL2Listener : public UObject { public: virtual ~UI3DL2Listener(); };
UI3DL2Listener::~UI3DL2Listener() { ConditionalDestroy(); }
class ACarriedObject : public AActor { public: virtual ~ACarriedObject(); };
ACarriedObject::~ACarriedObject() { ConditionalDestroy(); }
class AAntiPortalActor : public AActor { public: virtual ~AAntiPortalActor(); };
AAntiPortalActor::~AAntiPortalActor() { ConditionalDestroy(); }
class AReplicationInfo : public AInfo { public: virtual ~AReplicationInfo(); };
AReplicationInfo::~AReplicationInfo() { ConditionalDestroy(); }
class ABrush : public AActor { public: virtual ~ABrush(); };
ABrush::~ABrush() { ConditionalDestroy(); }
class ABlockingVolume : public AVolume { public: virtual ~ABlockingVolume(); };
ABlockingVolume::~ABlockingVolume() { ConditionalDestroy(); }
class ATriggers : public AActor { public: virtual ~ATriggers(); };
ATriggers::~ATriggers() { ConditionalDestroy(); }
class ALineOfSightTrigger : public ATriggers { public: virtual ~ALineOfSightTrigger(); };
ALineOfSightTrigger::~ALineOfSightTrigger() { ConditionalDestroy(); }
class AInternetInfo : public AInfo { public: virtual ~AInternetInfo(); };
AInternetInfo::~AInternetInfo() { ConditionalDestroy(); }
class ALocalMessage : public AInfo { public: virtual ~ALocalMessage(); };
ALocalMessage::~ALocalMessage() { ConditionalDestroy(); }
class APlayerStart : public AActor { public: virtual ~APlayerStart(); };
APlayerStart::~APlayerStart() { ConditionalDestroy(); }
class ASBulletImpact : public AProjector { public: virtual ~ASBulletImpact(); };
ASBulletImpact::~ASBulletImpact() { ConditionalDestroy(); }
class ASImpactEffectBase : public AActor { public: virtual ~ASImpactEffectBase(); };
ASImpactEffectBase::~ASImpactEffectBase() { ConditionalDestroy(); }
class AEffects : public AActor { public: virtual ~AEffects(); };
AEffects::~AEffects() { ConditionalDestroy(); }
class AProjectile : public AActor { public: virtual ~AProjectile(); };
AProjectile::~AProjectile() { ConditionalDestroy(); }
class ADecoration : public AActor { public: virtual ~ADecoration(); };
ADecoration::~ADecoration() { ConditionalDestroy(); }
class ASkyZoneInfo : public AZoneInfo { public: virtual ~ASkyZoneInfo(); };
ASkyZoneInfo::~ASkyZoneInfo() { ConditionalDestroy(); }
class ALight : public AActor { public: virtual ~ALight(); };
ALight::~ALight() { ConditionalDestroy(); }
class AGameStats : public AInfo { public: virtual ~AGameStats(); };
AGameStats::~AGameStats() { ConditionalDestroy(); }
class ASImpactManager : public AActor { public: virtual ~ASImpactManager(); };
ASImpactManager::~ASImpactManager() { ConditionalDestroy(); }
class UGUI : public UObject { public: virtual ~UGUI(); };
UGUI::~UGUI() { ConditionalDestroy(); }
class UmoEditBox : public UGUIMenuOption { public: virtual ~UmoEditBox(); };
UmoEditBox::~UmoEditBox() { ConditionalDestroy(); }
class UmoListEditBox : public UmoEditBox { public: virtual ~UmoListEditBox(); };
UmoListEditBox::~UmoListEditBox() { ConditionalDestroy(); }
class UmoListEditBox_GameMode : public UmoListEditBox { public: virtual ~UmoListEditBox_GameMode(); };
UmoListEditBox_GameMode::~UmoListEditBox_GameMode() { ConditionalDestroy(); }
class UStrategicMap : public UGUIPageWaitLaunch { public: virtual ~UStrategicMap(); };
UStrategicMap::~UStrategicMap() { ConditionalDestroy(); }
class UGUIListBase : public UGUIComponent { public: virtual ~UGUIListBase(); };
UGUIListBase::~UGUIListBase() { ConditionalDestroy(); }
class UGUIVertList : public UGUIListBase { public: virtual ~UGUIVertList(); };
UGUIVertList::~UGUIVertList() { ConditionalDestroy(); }
class UGUIScrollBarBase : public UGUIMultiComponent { public: virtual ~UGUIScrollBarBase(); };
UGUIScrollBarBase::~UGUIScrollBarBase() { ConditionalDestroy(); }
class UGUIVertScrollBarLabel : public UGUIScrollBarBase { public: virtual ~UGUIVertScrollBarLabel(); };
UGUIVertScrollBarLabel::~UGUIVertScrollBarLabel() { ConditionalDestroy(); }
class UGUIMultiPageLabelBox : public UGUIMultiComponent { public: virtual ~UGUIMultiPageLabelBox(); };
UGUIMultiPageLabelBox::~UGUIMultiPageLabelBox() { ConditionalDestroy(); }
class UGUIMOTDInfoBox : public UGUIMultiComponent { public: virtual ~UGUIMOTDInfoBox(); };
UGUIMOTDInfoBox::~UGUIMOTDInfoBox() { ConditionalDestroy(); }
class UGUISessionInfoBox : public UGUIMultiComponent { public: virtual ~UGUISessionInfoBox(); };
UGUISessionInfoBox::~UGUISessionInfoBox() { ConditionalDestroy(); }
class UGUISearchFilterBox : public UGUIMultiComponent { public: virtual ~UGUISearchFilterBox(); };
UGUISearchFilterBox::~UGUISearchFilterBox() { ConditionalDestroy(); }
class UGUICadre : public UGUIComponent { public: virtual ~UGUICadre(); };
UGUICadre::~UGUICadre() { ConditionalDestroy(); }
class UmoImageListMap : public UGUIMenuOption { public: virtual ~UmoImageListMap(); };
UmoImageListMap::~UmoImageListMap() { ConditionalDestroy(); }
class UGUIListBoxBase : public UGUIMultiComponent { public: virtual ~UGUIListBoxBase(); };
UGUIListBoxBase::~UGUIListBoxBase() { ConditionalDestroy(); }
class UGUIScrollTextBox : public UGUIListBoxBase { public: virtual ~UGUIScrollTextBox(); };
UGUIScrollTextBox::~UGUIScrollTextBox() { ConditionalDestroy(); }
class UGUICircularList : public UGUIListBase { public: virtual ~UGUICircularList(); };
UGUICircularList::~UGUICircularList() { ConditionalDestroy(); }
class UGUIHorzList : public UGUIListBase { public: virtual ~UGUIHorzList(); };
UGUIHorzList::~UGUIHorzList() { ConditionalDestroy(); }
class UGUIPanel : public UGUIMultiComponent { public: virtual ~UGUIPanel(); };
UGUIPanel::~UGUIPanel() { ConditionalDestroy(); }
class UGUISplitter : public UGUIPanel { public: virtual ~UGUISplitter(); };
UGUISplitter::~UGUISplitter() { ConditionalDestroy(); }
class UGUITabPanel : public UGUIPanel { public: virtual ~UGUITabPanel(); };
UGUITabPanel::~UGUITabPanel() { ConditionalDestroy(); }
class UGUITabButton : public UGUIButton { public: virtual ~UGUITabButton(); };
UGUITabButton::~UGUITabButton() { ConditionalDestroy(); }
class UGUIMultiColumnListHeader : public UGUIComponent { public: virtual ~UGUIMultiColumnListHeader(); };
UGUIMultiColumnListHeader::~UGUIMultiColumnListHeader() { ConditionalDestroy(); }
class UGUIListFriends : public UGUIList { public: virtual ~UGUIListFriends(); };
UGUIListFriends::~UGUIListFriends() { ConditionalDestroy(); }
class UGUIMultiColumnListBox : public UGUIListBoxBase { public: virtual ~UGUIMultiColumnListBox(); };
UGUIMultiColumnListBox::~UGUIMultiColumnListBox() { ConditionalDestroy(); }
class UGUIListBox : public UGUIListBoxBase { public: virtual ~UGUIListBox(); };
UGUIListBox::~UGUIListBox() { ConditionalDestroy(); }
class UGUIHorzScrollBar : public UGUIScrollBarBase { public: virtual ~UGUIHorzScrollBar(); };
UGUIHorzScrollBar::~UGUIHorzScrollBar() { ConditionalDestroy(); }
class UGUIGFXButton : public UGUIButton { public: virtual ~UGUIGFXButton(); };
UGUIGFXButton::~UGUIGFXButton() { ConditionalDestroy(); }
class UGUIHorzGripButton : public UGUIGFXButton { public: virtual ~UGUIHorzGripButton(); };
UGUIHorzGripButton::~UGUIHorzGripButton() { ConditionalDestroy(); }
class UGUIHorzScrollButton : public UGUIGFXButton { public: virtual ~UGUIHorzScrollButton(); };
UGUIHorzScrollButton::~UGUIHorzScrollButton() { ConditionalDestroy(); }
class UGUIHorzScrollZone : public UGUIComponent { public: virtual ~UGUIHorzScrollZone(); };
UGUIHorzScrollZone::~UGUIHorzScrollZone() { ConditionalDestroy(); }
class UGUIVertScrollBar : public UGUIScrollBarBase { public: virtual ~UGUIVertScrollBar(); };
UGUIVertScrollBar::~UGUIVertScrollBar() { ConditionalDestroy(); }
class UGUIVertGripButton : public UGUIGFXButton { public: virtual ~UGUIVertGripButton(); };
UGUIVertGripButton::~UGUIVertGripButton() { ConditionalDestroy(); }
class UGUIVertScrollButton : public UGUIGFXButton { public: virtual ~UGUIVertScrollButton(); };
UGUIVertScrollButton::~UGUIVertScrollButton() { ConditionalDestroy(); }
class UGUIVertScrollZone : public UGUIComponent { public: virtual ~UGUIVertScrollZone(); };
UGUIVertScrollZone::~UGUIVertScrollZone() { ConditionalDestroy(); }
class UGUICheckBoxButton : public UGUIGFXButton { public: virtual ~UGUICheckBoxButton(); };
UGUICheckBoxButton::~UGUICheckBoxButton() { ConditionalDestroy(); }
class UGUISpinnerButton : public UGUIGFXButton { public: virtual ~UGUISpinnerButton(); };
UGUISpinnerButton::~UGUISpinnerButton() { ConditionalDestroy(); }
class UGUILabelPad : public UGUILabel { public: virtual ~UGUILabelPad(); };
UGUILabelPad::~UGUILabelPad() { ConditionalDestroy(); }
class UGUIImage : public UGUIComponent { public: virtual ~UGUIImage(); };
UGUIImage::~UGUIImage() { ConditionalDestroy(); }
class ASFlagDropZoneInteraction : public ASInteractObject { public: virtual ~ASFlagDropZoneInteraction(); };
ASFlagDropZoneInteraction::~ASFlagDropZoneInteraction() { ConditionalDestroy(); }
class ASMines : public ASBasicGamePlayObject { public: virtual ~ASMines(); };
ASMines::~ASMines() { ConditionalDestroy(); }
class ASPresDetector : public ASMines { public: virtual ~ASPresDetector(); };
ASPresDetector::~ASPresDetector() { ConditionalDestroy(); }
class ASNotifyNoise : public AActor { public: virtual ~ASNotifyNoise(); };
ASNotifyNoise::~ASNotifyNoise() { ConditionalDestroy(); }
class ASDamageVolume : public AVolume { public: virtual ~ASDamageVolume(); };
ASDamageVolume::~ASDamageVolume() { ConditionalDestroy(); }
class ASDisk : public ASObjectiveTrigger { public: virtual ~ASDisk(); };
ASDisk::~ASDisk() { ConditionalDestroy(); }
class ASFlaque : public ASBasicGamePlayObject { public: virtual ~ASFlaque(); };
ASFlaque::~ASFlaque() { ConditionalDestroy(); }
class ASSprinkler : public ASBasicGamePlayObject { public: virtual ~ASSprinkler(); };
ASSprinkler::~ASSprinkler() { ConditionalDestroy(); }
class ASOrientableObject : public ASBasicGamePlayObject { public: virtual ~ASOrientableObject(); };
ASOrientableObject::~ASOrientableObject() { ConditionalDestroy(); }
class ASProjoFixe : public ASOrientableObject { public: virtual ~ASProjoFixe(); };
ASProjoFixe::~ASProjoFixe() { ConditionalDestroy(); }
class ASLiftDoor : public AMover { public: virtual ~ASLiftDoor(); };
ASLiftDoor::~ASLiftDoor() { ConditionalDestroy(); }
class ASInfirmary : public ASBasicGamePlayObject { public: virtual ~ASInfirmary(); };
ASInfirmary::~ASInfirmary() { ConditionalDestroy(); }
class ASFlashGen : public ALight { public: virtual ~ASFlashGen(); };
ASFlashGen::~ASFlashGen() { ConditionalDestroy(); }
class ASWeapon : public AInventory { public: virtual ~ASWeapon(); };
ASWeapon::~ASWeapon() { ConditionalDestroy(); }
class ASWeaponAttaque : public ASWeapon { public: virtual ~ASWeaponAttaque(); };
ASWeaponAttaque::~ASWeaponAttaque() { ConditionalDestroy(); }
class ASWeaponDefenseDummy : public AActor { public: virtual ~ASWeaponDefenseDummy(); };
ASWeaponDefenseDummy::~ASWeaponDefenseDummy() { ConditionalDestroy(); }
class ASTriggerInteraction : public ASInteractObject { public: virtual ~ASTriggerInteraction(); };
ASTriggerInteraction::~ASTriggerInteraction() { ConditionalDestroy(); }
class ASStickyCamEmitter : public AEmitter { public: virtual ~ASStickyCamEmitter(); };
ASStickyCamEmitter::~ASStickyCamEmitter() { ConditionalDestroy(); }
class ASGadget : public AInventory { public: virtual ~ASGadget(); };
ASGadget::~ASGadget() { ConditionalDestroy(); }
class ASSmokeLaunchEmitter : public AEmitter { public: virtual ~ASSmokeLaunchEmitter(); };
ASSmokeLaunchEmitter::~ASSmokeLaunchEmitter() { ConditionalDestroy(); }
class ASSmokeEmitter : public AEmitter { public: virtual ~ASSmokeEmitter(); };
ASSmokeEmitter::~ASSmokeEmitter() { ConditionalDestroy(); }
class ASSavedMove : public AInfo { public: virtual ~ASSavedMove(); };
ASSavedMove::~ASSavedMove() { ConditionalDestroy(); }
class ASRJ45Checker : public AActor { public: virtual ~ASRJ45Checker(); };
ASRJ45Checker::~ASRJ45Checker() { ConditionalDestroy(); }
class ASProjGrenade : public AProjectile { public: virtual ~ASProjGrenade(); };
ASProjGrenade::~ASProjGrenade() { ConditionalDestroy(); }
class ASProjSmoke : public ASProjGrenade { public: virtual ~ASProjSmoke(); };
ASProjSmoke::~ASProjSmoke() { ConditionalDestroy(); }
class ASProjPhospho : public ASProjGrenade { public: virtual ~ASProjPhospho(); };
ASProjPhospho::~ASProjPhospho() { ConditionalDestroy(); }
class ASProjFrag : public ASProjGrenade { public: virtual ~ASProjFrag(); };
ASProjFrag::~ASProjFrag() { ConditionalDestroy(); }
class ASProjFlashBang : public ASProjGrenade { public: virtual ~ASProjFlashBang(); };
ASProjFlashBang::~ASProjFlashBang() { ConditionalDestroy(); }
class ASProjFlare : public ASProjGrenade { public: virtual ~ASProjFlare(); };
ASProjFlare::~ASProjFlare() { ConditionalDestroy(); }
class ASProjChaffe : public ASProjGrenade { public: virtual ~ASProjChaffe(); };
ASProjChaffe::~ASProjChaffe() { ConditionalDestroy(); }
class ASPlayerCam : public AActor { public: virtual ~ASPlayerCam(); };
ASPlayerCam::~ASPlayerCam() { ConditionalDestroy(); }
class ASPickupInteraction : public ASInteractObject { public: virtual ~ASPickupInteraction(); };
ASPickupInteraction::~ASPickupInteraction() { ConditionalDestroy(); }
class ASPhosphoEmitter : public AEmitter { public: virtual ~ASPhosphoEmitter(); };
ASPhosphoEmitter::~ASPhosphoEmitter() { ConditionalDestroy(); }
class ASPAWNDEFENSE : public ASBasePawn { public: virtual ~ASPAWNDEFENSE(); };
ASPAWNDEFENSE::~ASPAWNDEFENSE() { ConditionalDestroy(); }
class ASPawnAttaque : public ASBasePawn { public: virtual ~ASPawnAttaque(); };
ASPawnAttaque::~ASPawnAttaque() { ConditionalDestroy(); }
class ASNoiseEmitter : public ASBasicGamePlayObject { public: virtual ~ASNoiseEmitter(); };
ASNoiseEmitter::~ASNoiseEmitter() { ConditionalDestroy(); }
class ASMuzzleFlash : public AActor { public: virtual ~ASMuzzleFlash(); };
ASMuzzleFlash::~ASMuzzleFlash() { ConditionalDestroy(); }
class ASMuzzleSticky : public ASMuzzleFlash { public: virtual ~ASMuzzleSticky(); };
ASMuzzleSticky::~ASMuzzleSticky() { ConditionalDestroy(); }
class ASMotionTrackingVision : public AMotionTrackingVision { public: virtual ~ASMotionTrackingVision(); };
ASMotionTrackingVision::~ASMotionTrackingVision() { ConditionalDestroy(); }
class ASMineProx : public ASMines { public: virtual ~ASMineProx(); };
ASMineProx::~ASMineProx() { ConditionalDestroy(); }
class ASMineLaser : public ASMines { public: virtual ~ASMineLaser(); };
ASMineLaser::~ASMineLaser() { ConditionalDestroy(); }
class USMatchInfo : public UMatchInfo { public: virtual ~USMatchInfo(); };
USMatchInfo::~USMatchInfo() { ConditionalDestroy(); }
class ASLeaveInteraction : public ASInteractObject { public: virtual ~ASLeaveInteraction(); };
ASLeaveInteraction::~ASLeaveInteraction() { ConditionalDestroy(); }
class ASLaserTrigger : public ASBasicGamePlayObject { public: virtual ~ASLaserTrigger(); };
ASLaserTrigger::~ASLaserTrigger() { ConditionalDestroy(); }
class ASLaserProjector : public AProjector { public: virtual ~ASLaserProjector(); };
ASLaserProjector::~ASLaserProjector() { ConditionalDestroy(); }
class ASLaserBeam : public AEffects { public: virtual ~ASLaserBeam(); };
ASLaserBeam::~ASLaserBeam() { ConditionalDestroy(); }
class ASLaserBeam_MineLaser : public ASLaserBeam { public: virtual ~ASLaserBeam_MineLaser(); };
ASLaserBeam_MineLaser::~ASLaserBeam_MineLaser() { ConditionalDestroy(); }
class ASInteractionManager : public AActor { public: virtual ~ASInteractionManager(); };
ASInteractionManager::~ASInteractionManager() { ConditionalDestroy(); }
class ASGrenadeExplosion : public AEmitter { public: virtual ~ASGrenadeExplosion(); };
ASGrenadeExplosion::~ASGrenadeExplosion() { ConditionalDestroy(); }
class ASGoggleBeam : public AActor { public: virtual ~ASGoggleBeam(); };
ASGoggleBeam::~ASGoggleBeam() { ConditionalDestroy(); }
class ASGadTazerLauncher : public ASGadget { public: virtual ~ASGadTazerLauncher(); };
ASGadTazerLauncher::~ASGadTazerLauncher() { ConditionalDestroy(); }
class ASGadNoiseEmitter : public ASGadget { public: virtual ~ASGadNoiseEmitter(); };
ASGadNoiseEmitter::~ASGadNoiseEmitter() { ConditionalDestroy(); }
class ASGadGrenLauncher : public ASGadget { public: virtual ~ASGadGrenLauncher(); };
ASGadGrenLauncher::~ASGadGrenLauncher() { ConditionalDestroy(); }
class ASGadBomb : public ASGadget { public: virtual ~ASGadBomb(); };
ASGadBomb::~ASGadBomb() { ConditionalDestroy(); }
class ASFragLaunchEmitter : public AEmitter { public: virtual ~ASFragLaunchEmitter(); };
ASFragLaunchEmitter::~ASFragLaunchEmitter() { ConditionalDestroy(); }
class ASFlashBang : public AEmitter { public: virtual ~ASFlashBang(); };
ASFlashBang::~ASFlashBang() { ConditionalDestroy(); }
class ASFlagDropZone : public ASObjectiveTrigger { public: virtual ~ASFlagDropZone(); };
ASFlagDropZone::~ASFlagDropZone() { ConditionalDestroy(); }
class ASFlag : public ASObjectiveTrigger { public: virtual ~ASFlag(); };
ASFlag::~ASFlag() { ConditionalDestroy(); }
class ASFireTrace : public AEffects { public: virtual ~ASFireTrace(); };
ASFireTrace::~ASFireTrace() { ConditionalDestroy(); }
class ASComputerObjectiveTrigger : public ASObjectiveTrigger { public: virtual ~ASComputerObjectiveTrigger(); };
ASComputerObjectiveTrigger::~ASComputerObjectiveTrigger() { ConditionalDestroy(); }
class ASChaffeEmitter : public AEmitter { public: virtual ~ASChaffeEmitter(); };
ASChaffeEmitter::~ASChaffeEmitter() { ConditionalDestroy(); }
class ASCamera : public ASBasicGamePlayObject { public: virtual ~ASCamera(); };
ASCamera::~ASCamera() { ConditionalDestroy(); }
class ASBombTargetObjectiveTrigger : public ASObjectiveTrigger { public: virtual ~ASBombTargetObjectiveTrigger(); };
ASBombTargetObjectiveTrigger::~ASBombTargetObjectiveTrigger() { ConditionalDestroy(); }
class ASBloodSplat : public AProjector { public: virtual ~ASBloodSplat(); };
ASBloodSplat::~ASBloodSplat() { ConditionalDestroy(); }
class ASAmmoStock : public ASBasicGamePlayObject { public: virtual ~ASAmmoStock(); };
ASAmmoStock::~ASAmmoStock() { ConditionalDestroy(); }
class ASAmbientSoundTrigger : public ATriggers { public: virtual ~ASAmbientSoundTrigger(); };
ASAmbientSoundTrigger::~ASAmbientSoundTrigger() { ConditionalDestroy(); }
class ASFlareEmitter : public AEmitter { public: virtual ~ASFlareEmitter(); };
ASFlareEmitter::~ASFlareEmitter() { ConditionalDestroy(); }
class ASFlareCourseEmitter : public AEmitter { public: virtual ~ASFlareCourseEmitter(); };
ASFlareCourseEmitter::~ASFlareCourseEmitter() { ConditionalDestroy(); }
class ASExtincteurEmitter : public AEmitter { public: virtual ~ASExtincteurEmitter(); };
ASExtincteurEmitter::~ASExtincteurEmitter() { ConditionalDestroy(); }
class ASFlashLight : public ALight { public: virtual ~ASFlashLight(); };
ASFlashLight::~ASFlashLight() { ConditionalDestroy(); }
class ASFlashLight_FragGrenade : public ASFlashLight { public: virtual ~ASFlashLight_FragGrenade(); };
ASFlashLight_FragGrenade::~ASFlashLight_FragGrenade() { ConditionalDestroy(); }
class ASDouilleEmitter : public AEmitter { public: virtual ~ASDouilleEmitter(); };
ASDouilleEmitter::~ASDouilleEmitter() { ConditionalDestroy(); }
class ASTazerEmitter : public AEffects { public: virtual ~ASTazerEmitter(); };
ASTazerEmitter::~ASTazerEmitter() { ConditionalDestroy(); }
class ASMinePiegeSpy : public ASMines { public: virtual ~ASMinePiegeSpy(); };
ASMinePiegeSpy::~ASMinePiegeSpy() { ConditionalDestroy(); }
class ASAmmoStockInteraction : public ASInteractObject { public: virtual ~ASAmmoStockInteraction(); };
ASAmmoStockInteraction::~ASAmmoStockInteraction() { ConditionalDestroy(); }
class ASMineProxInteraction : public ASInteractObject { public: virtual ~ASMineProxInteraction(); };
ASMineProxInteraction::~ASMineProxInteraction() { ConditionalDestroy(); }
class ASExtincteur : public AActor { public: virtual ~ASExtincteur(); };
ASExtincteur::~ASExtincteur() { ConditionalDestroy(); }
class ASGadSmokeGrenade : public ASGadGrenLauncher { public: virtual ~ASGadSmokeGrenade(); };
ASGadSmokeGrenade::~ASGadSmokeGrenade() { ConditionalDestroy(); }
class ASGadPhosphoLauncher : public ASGadGrenLauncher { public: virtual ~ASGadPhosphoLauncher(); };
ASGadPhosphoLauncher::~ASGadPhosphoLauncher() { ConditionalDestroy(); }
class ASGadFragGrenade : public ASGadGrenLauncher { public: virtual ~ASGadFragGrenade(); };
ASGadFragGrenade::~ASGadFragGrenade() { ConditionalDestroy(); }
class ASGadFlareLauncher : public ASGadGrenLauncher { public: virtual ~ASGadFlareLauncher(); };
ASGadFlareLauncher::~ASGadFlareLauncher() { ConditionalDestroy(); }
class ASGadChaffeGrenade : public ASGadGrenLauncher { public: virtual ~ASGadChaffeGrenade(); };
ASGadChaffeGrenade::~ASGadChaffeGrenade() { ConditionalDestroy(); }
class ASGadFlashBang : public ASGadGrenLauncher { public: virtual ~ASGadFlashBang(); };
ASGadFlashBang::~ASGadFlashBang() { ConditionalDestroy(); }
class ASGadCamNetwork : public ASGadget { public: virtual ~ASGadCamNetwork(); };
ASGadCamNetwork::~ASGadCamNetwork() { ConditionalDestroy(); }
class ASStickyCam : public ASOrientableObject { public: virtual ~ASStickyCam(); };
ASStickyCam::~ASStickyCam() { ConditionalDestroy(); }
class ASWeaponDefense_Shotgun : public ASWeaponDefense { public: virtual ~ASWeaponDefense_Shotgun(); };
ASWeaponDefense_Shotgun::~ASWeaponDefense_Shotgun() { ConditionalDestroy(); }
class ASImpactEffectShockerMetal : public ASImpactEffectBase { public: virtual ~ASImpactEffectShockerMetal(); };
ASImpactEffectShockerMetal::~ASImpactEffectShockerMetal() { ConditionalDestroy(); }
class ASImpactEffectShocker : public ASImpactEffectBase { public: virtual ~ASImpactEffectShocker(); };
ASImpactEffectShocker::~ASImpactEffectShocker() { ConditionalDestroy(); }
class ASMinePoison : public ASMinePiegeSpy { public: virtual ~ASMinePoison(); };
ASMinePoison::~ASMinePoison() { ConditionalDestroy(); }
class ASMateInteraction : public ASInteractObject { public: virtual ~ASMateInteraction(); };
ASMateInteraction::~ASMateInteraction() { ConditionalDestroy(); }
class ASHealMateInteraction : public ASMateInteraction { public: virtual ~ASHealMateInteraction(); };
ASHealMateInteraction::~ASHealMateInteraction() { ConditionalDestroy(); }
class ASBackPackInteraction : public ASMateInteraction { public: virtual ~ASBackPackInteraction(); };
ASBackPackInteraction::~ASBackPackInteraction() { ConditionalDestroy(); }
class ASRemoveSpyBulletInteraction : public ASMateInteraction { public: virtual ~ASRemoveSpyBulletInteraction(); };
ASRemoveSpyBulletInteraction::~ASRemoveSpyBulletInteraction() { ConditionalDestroy(); }
class ASWeaponDefense_Uzi : public ASWeaponDefense { public: virtual ~ASWeaponDefense_Uzi(); };
ASWeaponDefense_Uzi::~ASWeaponDefense_Uzi() { ConditionalDestroy(); }
class ASWeaponDefenseDummy_Shotgun : public ASWeaponDefenseDummy { public: virtual ~ASWeaponDefenseDummy_Shotgun(); };
ASWeaponDefenseDummy_Shotgun::~ASWeaponDefenseDummy_Shotgun() { ConditionalDestroy(); }
class ASWeaponDefenseDummy_Uzi : public ASWeaponDefenseDummy { public: virtual ~ASWeaponDefenseDummy_Uzi(); };
ASWeaponDefenseDummy_Uzi::~ASWeaponDefenseDummy_Uzi() { ConditionalDestroy(); }
class ASPoisonEmitter : public AEmitter { public: virtual ~ASPoisonEmitter(); };
ASPoisonEmitter::~ASPoisonEmitter() { ConditionalDestroy(); }
class ASGadHeartBeatSensor : public ASGadget { public: virtual ~ASGadHeartBeatSensor(); };
ASGadHeartBeatSensor::~ASGadHeartBeatSensor() { ConditionalDestroy(); }
class ASCoronaLight : public ALight { public: virtual ~ASCoronaLight(); };
ASCoronaLight::~ASCoronaLight() { ConditionalDestroy(); }
class ASStickyCamInteraction : public ASMineProxInteraction { public: virtual ~ASStickyCamInteraction(); };
ASStickyCamInteraction::~ASStickyCamInteraction() { ConditionalDestroy(); }
class ASVitre : public ASGamePlayObject { public: virtual ~ASVitre(); };
ASVitre::~ASVitre() { ConditionalDestroy(); }
class AESBPatchActor : public AESoftBodyActor { public: virtual ~AESBPatchActor(); };
AESBPatchActor::~AESBPatchActor() { ConditionalDestroy(); }
class AESBStripDoorActor : public AESBPatchActor { public: virtual ~AESBStripDoorActor(); };
AESBStripDoorActor::~AESBStripDoorActor() { ConditionalDestroy(); }
class AESBRopeActor : public AESoftBodyActor { public: virtual ~AESBRopeActor(); };
AESBRopeActor::~AESBRopeActor() { ConditionalDestroy(); }
class AESBChainActor : public AESBRopeActor { public: virtual ~AESBChainActor(); };
AESBChainActor::~AESBChainActor() { ConditionalDestroy(); }
