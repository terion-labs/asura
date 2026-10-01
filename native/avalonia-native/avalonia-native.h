#pragma once
#include "com.h"
#include "stddef.h"
struct AvnSize;
struct AvnPixelSize;
struct AvnRect;
struct AvnVector;
struct AvnPoint;
struct AvnScreen;
struct AvnFramebuffer;
struct AvnColor;
struct IAvaloniaNativeFactory;
struct IAvnString;
struct IAvnTopLevel;
struct IAvnWindowBase;
struct IAvnPopup;
struct IAvnWindow;
struct IAvnTopLevelEvents;
struct IAvnWindowBaseEvents;
struct IAvnWindowEvents;
struct IAvnTextInputMethodClient;
struct IAvnTextInputMethod;
struct IAvnMacOptions;
struct IAvnActionCallback;
struct IAvnPlatformThreadingInterfaceEvents;
struct IAvnLoopCancellation;
struct IAvnPlatformThreadingInterface;
struct IAvnSystemDialogEvents;
struct IAvnStorageProvider;
struct IAvnFilePickerFileTypes;
struct IAvnScreenEvents;
struct IAvnScreens;
struct IAvnClipboard;
struct IAvnClipboardDataSource;
struct IAvnClipboardDataItem;
struct IAvnClipboardDataValue;
struct IAvnCursor;
struct IAvnCursorFactory;
struct IAvnSoftwareRenderTarget;
struct IAvnGlDisplay;
struct IAvnGlContext;
struct IAvnGlSurfaceRenderTarget;
struct IAvnGlSurfaceRenderingSession;
struct IAvnMetalDisplay;
struct IAvnMetalDevice;
struct IAvnMetalRenderTarget;
struct IAvnMTLSharedEvent;
struct IAvnMetalTexture;
struct IAvnNativeObjectsMemoryManagement;
struct IAvnMetalRenderingSession;
struct IAvnTrayIcon;
struct IAvnMenu;
struct IAvnPredicateCallback;
struct IAvnMenuItem;
struct IAvnMenuEvents;
struct IAvnStringArray;
struct IAvnDndResultCallback;
struct IAvnGCHandleDeallocatorCallback;
struct IAvnDispatcher;
struct IAvnNativeControlHost;
struct IAvnNativeControlHostTopLevelAttachment;
struct IAvnApplicationEvents;
struct IAvnApplicationCommands;
struct IAvnAutomationPeer;
struct IAvnAutomationPeerArray;
struct IAvnAutomationNode;
struct IAvnPlatformSettings;
struct IAvnPlatformBehaviorInhibition;
struct IAvnPlatformRenderTimer;
enum AvnKey
{
    AvnKeyNone = 0,
    AvnKeyCancel = 1,
    AvnKeyBack = 2,
    AvnKeyTab = 3,
    AvnKeyLineFeed = 4,
    AvnKeyClear = 5,
    AvnKeyReturn = 6,
    AvnKeyEnter = 6,
    AvnKeyPause = 7,
    AvnKeyCapsLock = 8,
    AvnKeyCapital = 8,
    AvnKeyHangulMode = 9,
    AvnKeyKanaMode = 9,
    AvnKeyJunjaMode = 10,
    AvnKeyFinalMode = 11,
    AvnKeyKanjiMode = 12,
    AvnKeyHanjaMode = 12,
    AvnKeyEscape = 13,
    AvnKeyImeConvert = 14,
    AvnKeyImeNonConvert = 15,
    AvnKeyImeAccept = 16,
    AvnKeyImeModeChange = 17,
    AvnKeySpace = 18,
    AvnKeyPageUp = 19,
    AvnKeyPrior = 19,
    AvnKeyPageDown = 20,
    AvnKeyNext = 20,
    AvnKeyEnd = 21,
    AvnKeyHome = 22,
    AvnKeyLeft = 23,
    AvnKeyUp = 24,
    AvnKeyRight = 25,
    AvnKeyDown = 26,
    AvnKeySelect = 27,
    AvnKeyPrint = 28,
    AvnKeyExecute = 29,
    AvnKeySnapshot = 30,
    AvnKeyPrintScreen = 30,
    AvnKeyInsert = 31,
    AvnKeyDelete = 32,
    AvnKeyHelp = 33,
    AvnKeyD0 = 34,
    AvnKeyD1 = 35,
    AvnKeyD2 = 36,
    AvnKeyD3 = 37,
    AvnKeyD4 = 38,
    AvnKeyD5 = 39,
    AvnKeyD6 = 40,
    AvnKeyD7 = 41,
    AvnKeyD8 = 42,
    AvnKeyD9 = 43,
    AvnKeyA = 44,
    AvnKeyB = 45,
    AvnKeyC = 46,
    AvnKeyD = 47,
    AvnKeyE = 48,
    AvnKeyF = 49,
    AvnKeyG = 50,
    AvnKeyH = 51,
    AvnKeyI = 52,
    AvnKeyJ = 53,
    AvnKeyK = 54,
    AvnKeyL = 55,
    AvnKeyM = 56,
    AvnKeyN = 57,
    AvnKeyO = 58,
    AvnKeyP = 59,
    AvnKeyQ = 60,
    AvnKeyR = 61,
    AvnKeyS = 62,
    AvnKeyT = 63,
    AvnKeyU = 64,
    AvnKeyV = 65,
    AvnKeyW = 66,
    AvnKeyX = 67,
    AvnKeyY = 68,
    AvnKeyZ = 69,
    AvnKeyLWin = 70,
    AvnKeyRWin = 71,
    AvnKeyApps = 72,
    AvnKeySleep = 73,
    AvnKeyNumPad0 = 74,
    AvnKeyNumPad1 = 75,
    AvnKeyNumPad2 = 76,
    AvnKeyNumPad3 = 77,
    AvnKeyNumPad4 = 78,
    AvnKeyNumPad5 = 79,
    AvnKeyNumPad6 = 80,
    AvnKeyNumPad7 = 81,
    AvnKeyNumPad8 = 82,
    AvnKeyNumPad9 = 83,
    AvnKeyMultiply = 84,
    AvnKeyAdd = 85,
    AvnKeySeparator = 86,
    AvnKeySubtract = 87,
    AvnKeyDecimal = 88,
    AvnKeyDivide = 89,
    AvnKeyF1 = 90,
    AvnKeyF2 = 91,
    AvnKeyF3 = 92,
    AvnKeyF4 = 93,
    AvnKeyF5 = 94,
    AvnKeyF6 = 95,
    AvnKeyF7 = 96,
    AvnKeyF8 = 97,
    AvnKeyF9 = 98,
    AvnKeyF10 = 99,
    AvnKeyF11 = 100,
    AvnKeyF12 = 101,
    AvnKeyF13 = 102,
    AvnKeyF14 = 103,
    AvnKeyF15 = 104,
    AvnKeyF16 = 105,
    AvnKeyF17 = 106,
    AvnKeyF18 = 107,
    AvnKeyF19 = 108,
    AvnKeyF20 = 109,
    AvnKeyF21 = 110,
    AvnKeyF22 = 111,
    AvnKeyF23 = 112,
    AvnKeyF24 = 113,
    AvnKeyNumLock = 114,
    AvnKeyScroll = 115,
    AvnKeyLeftShift = 116,
    AvnKeyRightShift = 117,
    AvnKeyLeftCtrl = 118,
    AvnKeyRightCtrl = 119,
    AvnKeyLeftAlt = 120,
    AvnKeyRightAlt = 121,
    AvnKeyBrowserBack = 122,
    AvnKeyBrowserForward = 123,
    AvnKeyBrowserRefresh = 124,
    AvnKeyBrowserStop = 125,
    AvnKeyBrowserSearch = 126,
    AvnKeyBrowserFavorites = 127,
    AvnKeyBrowserHome = 128,
    AvnKeyVolumeMute = 129,
    AvnKeyVolumeDown = 130,
    AvnKeyVolumeUp = 131,
    AvnKeyMediaNextTrack = 132,
    AvnKeyMediaPreviousTrack = 133,
    AvnKeyMediaStop = 134,
    AvnKeyMediaPlayPause = 135,
    AvnKeyLaunchMail = 136,
    AvnKeySelectMedia = 137,
    AvnKeyLaunchApplication1 = 138,
    AvnKeyLaunchApplication2 = 139,
    AvnKeyOemSemicolon = 140,
    AvnKeyOem1 = 140,
    AvnKeyOemPlus = 141,
    AvnKeyOemComma = 142,
    AvnKeyOemMinus = 143,
    AvnKeyOemPeriod = 144,
    AvnKeyOemQuestion = 145,
    AvnKeyOem2 = 145,
    AvnKeyOemTilde = 146,
    AvnKeyOem3 = 146,
    AvnKeyAbntC1 = 147,
    AvnKeyAbntC2 = 148,
    AvnKeyOemOpenBrackets = 149,
    AvnKeyOem4 = 149,
    AvnKeyOemPipe = 150,
    AvnKeyOem5 = 150,
    AvnKeyOemCloseBrackets = 151,
    AvnKeyOem6 = 151,
    AvnKeyOemQuotes = 152,
    AvnKeyOem7 = 152,
    AvnKeyOem8 = 153,
    AvnKeyOemBackslash = 154,
    AvnKeyOem102 = 154,
    AvnKeyImeProcessed = 155,
    AvnKeySystem = 156,
    AvnKeyOemAttn = 157,
    AvnKeyDbeAlphanumeric = 157,
    AvnKeyOemFinish = 158,
    AvnKeyDbeKatakana = 158,
    AvnKeyDbeHiragana = 159,
    AvnKeyOemCopy = 159,
    AvnKeyDbeSbcsChar = 160,
    AvnKeyOemAuto = 160,
    AvnKeyDbeDbcsChar = 161,
    AvnKeyOemEnlw = 161,
    AvnKeyOemBackTab = 162,
    AvnKeyDbeRoman = 162,
    AvnKeyDbeNoRoman = 163,
    AvnKeyAttn = 163,
    AvnKeyCrSel = 164,
    AvnKeyDbeEnterWordRegisterMode = 164,
    AvnKeyExSel = 165,
    AvnKeyDbeEnterImeConfigureMode = 165,
    AvnKeyEraseEof = 166,
    AvnKeyDbeFlushString = 166,
    AvnKeyPlay = 167,
    AvnKeyDbeCodeInput = 167,
    AvnKeyDbeNoCodeInput = 168,
    AvnKeyZoom = 168,
    AvnKeyNoName = 169,
    AvnKeyDbeDetermineString = 169,
    AvnKeyDbeEnterDialogConversionMode = 170,
    AvnKeyPa1 = 170,
    AvnKeyOemClear = 171,
    AvnKeyDeadCharProcessed = 172,
    AvnKeyFnLeftArrow = 10001,
    AvnKeyFnRightArrow = 10002,
    AvnKeyFnUpArrow = 10003,
    AvnKeyFnDownArrow = 10004,
};
enum AvnPhysicalKey
{
    AvnPhysicalKeyNone = 0,
    AvnPhysicalKeyBackquote = 1,
    AvnPhysicalKeyBackslash = 2,
    AvnPhysicalKeyBracketLeft = 3,
    AvnPhysicalKeyBracketRight = 4,
    AvnPhysicalKeyComma = 5,
    AvnPhysicalKeyDigit0 = 6,
    AvnPhysicalKeyDigit1 = 7,
    AvnPhysicalKeyDigit2 = 8,
    AvnPhysicalKeyDigit3 = 9,
    AvnPhysicalKeyDigit4 = 10,
    AvnPhysicalKeyDigit5 = 11,
    AvnPhysicalKeyDigit6 = 12,
    AvnPhysicalKeyDigit7 = 13,
    AvnPhysicalKeyDigit8 = 14,
    AvnPhysicalKeyDigit9 = 15,
    AvnPhysicalKeyEqual = 16,
    AvnPhysicalKeyIntlBackslash = 17,
    AvnPhysicalKeyIntlRo = 18,
    AvnPhysicalKeyIntlYen = 19,
    AvnPhysicalKeyA = 20,
    AvnPhysicalKeyB = 21,
    AvnPhysicalKeyC = 22,
    AvnPhysicalKeyD = 23,
    AvnPhysicalKeyE = 24,
    AvnPhysicalKeyF = 25,
    AvnPhysicalKeyG = 26,
    AvnPhysicalKeyH = 27,
    AvnPhysicalKeyI = 28,
    AvnPhysicalKeyJ = 29,
    AvnPhysicalKeyK = 30,
    AvnPhysicalKeyL = 31,
    AvnPhysicalKeyM = 32,
    AvnPhysicalKeyN = 33,
    AvnPhysicalKeyO = 34,
    AvnPhysicalKeyP = 35,
    AvnPhysicalKeyQ = 36,
    AvnPhysicalKeyR = 37,
    AvnPhysicalKeyS = 38,
    AvnPhysicalKeyT = 39,
    AvnPhysicalKeyU = 40,
    AvnPhysicalKeyV = 41,
    AvnPhysicalKeyW = 42,
    AvnPhysicalKeyX = 43,
    AvnPhysicalKeyY = 44,
    AvnPhysicalKeyZ = 45,
    AvnPhysicalKeyMinus = 46,
    AvnPhysicalKeyPeriod = 47,
    AvnPhysicalKeyQuote = 48,
    AvnPhysicalKeySemicolon = 49,
    AvnPhysicalKeySlash = 50,
    AvnPhysicalKeyAltLeft = 51,
    AvnPhysicalKeyAltRight = 52,
    AvnPhysicalKeyBackspace = 53,
    AvnPhysicalKeyCapsLock = 54,
    AvnPhysicalKeyContextMenu = 55,
    AvnPhysicalKeyControlLeft = 56,
    AvnPhysicalKeyControlRight = 57,
    AvnPhysicalKeyEnter = 58,
    AvnPhysicalKeyMetaLeft = 59,
    AvnPhysicalKeyMetaRight = 60,
    AvnPhysicalKeyShiftLeft = 61,
    AvnPhysicalKeyShiftRight = 62,
    AvnPhysicalKeySpace = 63,
    AvnPhysicalKeyTab = 64,
    AvnPhysicalKeyConvert = 65,
    AvnPhysicalKeyKanaMode = 66,
    AvnPhysicalKeyLang1 = 67,
    AvnPhysicalKeyLang2 = 68,
    AvnPhysicalKeyLang3 = 69,
    AvnPhysicalKeyLang4 = 70,
    AvnPhysicalKeyLang5 = 71,
    AvnPhysicalKeyNonConvert = 72,
    AvnPhysicalKeyDelete = 73,
    AvnPhysicalKeyEnd = 74,
    AvnPhysicalKeyHelp = 75,
    AvnPhysicalKeyHome = 76,
    AvnPhysicalKeyInsert = 77,
    AvnPhysicalKeyPageDown = 78,
    AvnPhysicalKeyPageUp = 79,
    AvnPhysicalKeyArrowDown = 80,
    AvnPhysicalKeyArrowLeft = 81,
    AvnPhysicalKeyArrowRight = 82,
    AvnPhysicalKeyArrowUp = 83,
    AvnPhysicalKeyNumLock = 84,
    AvnPhysicalKeyNumPad0 = 85,
    AvnPhysicalKeyNumPad1 = 86,
    AvnPhysicalKeyNumPad2 = 87,
    AvnPhysicalKeyNumPad3 = 88,
    AvnPhysicalKeyNumPad4 = 89,
    AvnPhysicalKeyNumPad5 = 90,
    AvnPhysicalKeyNumPad6 = 91,
    AvnPhysicalKeyNumPad7 = 92,
    AvnPhysicalKeyNumPad8 = 93,
    AvnPhysicalKeyNumPad9 = 94,
    AvnPhysicalKeyNumPadAdd = 95,
    AvnPhysicalKeyNumPadClear = 96,
    AvnPhysicalKeyNumPadComma = 97,
    AvnPhysicalKeyNumPadDecimal = 98,
    AvnPhysicalKeyNumPadDivide = 99,
    AvnPhysicalKeyNumPadEnter = 100,
    AvnPhysicalKeyNumPadEqual = 101,
    AvnPhysicalKeyNumPadMultiply = 102,
    AvnPhysicalKeyNumPadParenLeft = 103,
    AvnPhysicalKeyNumPadParenRight = 104,
    AvnPhysicalKeyNumPadSubtract = 105,
    AvnPhysicalKeyEscape = 106,
    AvnPhysicalKeyF1 = 107,
    AvnPhysicalKeyF2 = 108,
    AvnPhysicalKeyF3 = 109,
    AvnPhysicalKeyF4 = 110,
    AvnPhysicalKeyF5 = 111,
    AvnPhysicalKeyF6 = 112,
    AvnPhysicalKeyF7 = 113,
    AvnPhysicalKeyF8 = 114,
    AvnPhysicalKeyF9 = 115,
    AvnPhysicalKeyF10 = 116,
    AvnPhysicalKeyF11 = 117,
    AvnPhysicalKeyF12 = 118,
    AvnPhysicalKeyF13 = 119,
    AvnPhysicalKeyF14 = 120,
    AvnPhysicalKeyF15 = 121,
    AvnPhysicalKeyF16 = 122,
    AvnPhysicalKeyF17 = 123,
    AvnPhysicalKeyF18 = 124,
    AvnPhysicalKeyF19 = 125,
    AvnPhysicalKeyF20 = 126,
    AvnPhysicalKeyF21 = 127,
    AvnPhysicalKeyF22 = 128,
    AvnPhysicalKeyF23 = 129,
    AvnPhysicalKeyF24 = 130,
    AvnPhysicalKeyPrintScreen = 131,
    AvnPhysicalKeyScrollLock = 132,
    AvnPhysicalKeyPause = 133,
    AvnPhysicalKeyBrowserBack = 134,
    AvnPhysicalKeyBrowserFavorites = 135,
    AvnPhysicalKeyBrowserForward = 136,
    AvnPhysicalKeyBrowserHome = 137,
    AvnPhysicalKeyBrowserRefresh = 138,
    AvnPhysicalKeyBrowserSearch = 139,
    AvnPhysicalKeyBrowserStop = 140,
    AvnPhysicalKeyEject = 141,
    AvnPhysicalKeyLaunchApp1 = 142,
    AvnPhysicalKeyLaunchApp2 = 143,
    AvnPhysicalKeyLaunchMail = 144,
    AvnPhysicalKeyMediaPlayPause = 145,
    AvnPhysicalKeyMediaSelect = 146,
    AvnPhysicalKeyMediaStop = 147,
    AvnPhysicalKeyMediaTrackNext = 148,
    AvnPhysicalKeyMediaTrackPrevious = 149,
    AvnPhysicalKeyPower = 150,
    AvnPhysicalKeySleep = 151,
    AvnPhysicalKeyAudioVolumeDown = 152,
    AvnPhysicalKeyAudioVolumeMute = 153,
    AvnPhysicalKeyAudioVolumeUp = 154,
    AvnPhysicalKeyWakeUp = 155,
    AvnPhysicalKeyAgain = 156,
    AvnPhysicalKeyCopy = 157,
    AvnPhysicalKeyCut = 158,
    AvnPhysicalKeyFind = 159,
    AvnPhysicalKeyOpen = 160,
    AvnPhysicalKeyPaste = 161,
    AvnPhysicalKeyProps = 162,
    AvnPhysicalKeySelect = 163,
    AvnPhysicalKeyUndo = 164,
};
enum SystemDecorations
{
    SystemDecorationsNone = 0,
    SystemDecorationsBorderOnly = 1,
    SystemDecorationsFull = 2,
};
enum AvnAutomationProperty
{
    AutomationPeer_AutomationId,
    AutomationPeer_BoundingRectangle,
    AutomationPeer_ClassName,
    AutomationPeer_Name,
    RangeValueProvider_Value,
    ValueProvider_Value,
    ToggleProvider_ToggleState,
    ExpandCollapseProvider_ExpandCollapseState,
    SelectionItemProvider_IsSelected,
    SelectionProvider_Selection,
};
enum AvnScreenOrientation
{
    UnknownOrientation,
    Landscape,
    Portrait,
    LandscapeFlipped,
    PortraitFlipped,
};
enum AvnPixelFormat
{
    kAvnRgb565,
    kAvnRgba8888,
    kAvnBgra8888,
};
enum AvnRawMouseEventType
{
    LeaveWindow,
    LeftButtonDown,
    LeftButtonUp,
    RightButtonDown,
    RightButtonUp,
    MiddleButtonDown,
    MiddleButtonUp,
    XButton1Down,
    XButton1Up,
    XButton2Down,
    XButton2Up,
    Move,
    Wheel,
    NonClientLeftButtonDown,
    TouchBegin,
    TouchUpdate,
    TouchEnd,
    TouchCancel,
    Magnify,
    Rotate,
    Swipe,
};
enum AvnRawKeyEventType
{
    KeyDown,
    KeyUp,
};
enum AvnInputModifiers
{
    AvnInputModifiersNone = 0,
    Alt = 1,
    Control = 2,
    Shift = 4,
    Windows = 8,
    LeftMouseButton = 16,
    RightMouseButton = 32,
    MiddleMouseButton = 64,
    XButton1MouseButton = 128,
    XButton2MouseButton = 256,
};
enum class AvnDragDropEffects
{
    None = 0,
    Copy = 1,
    Move = 2,
    Link = 4,
};
enum class AvnDragEventType
{
    Enter,
    Over,
    Leave,
    Drop,
};
enum AvnWindowState
{
    Normal,
    Minimized,
    Maximized,
    FullScreen,
};
enum AvnStandardCursorType
{
    CursorArrow,
    CursorIbeam,
    CursorWait,
    CursorCross,
    CursorUpArrow,
    CursorSizeWestEast,
    CursorSizeNorthSouth,
    CursorSizeAll,
    CursorNo,
    CursorHand,
    CursorAppStarting,
    CursorHelp,
    CursorTopSide,
    CursorBottomSize,
    CursorLeftSide,
    CursorRightSide,
    CursorTopLeftCorner,
    CursorTopRightCorner,
    CursorBottomLeftCorner,
    CursorBottomRightCorner,
    CursorDragMove,
    CursorDragCopy,
    CursorDragLink,
    CursorNone,
};
enum AvnWindowEdge
{
    WindowEdgeNorthWest,
    WindowEdgeNorth,
    WindowEdgeNorthEast,
    WindowEdgeWest,
    WindowEdgeEast,
    WindowEdgeSouthWest,
    WindowEdgeSouth,
    WindowEdgeSouthEast,
};
enum AvnMenuItemToggleType
{
    None,
    CheckMark,
    Radio,
};
enum AvnPlatformResizeReason
{
    ResizeUnspecified,
    ResizeUser,
    ResizeApplication,
    ResizeLayout,
    ResizeDpiChange,
};
enum AvnAutomationControlType
{
    AutomationNone,
    AutomationButton,
    AutomationCalendar,
    AutomationCheckBox,
    AutomationComboBox,
    AutomationComboBoxItem,
    AutomationEdit,
    AutomationHyperlink,
    AutomationImage,
    AutomationListItem,
    AutomationList,
    AutomationMenu,
    AutomationMenuBar,
    AutomationMenuItem,
    AutomationProgressBar,
    AutomationRadioButton,
    AutomationScrollBar,
    AutomationSlider,
    AutomationSpinner,
    AutomationStatusBar,
    AutomationTab,
    AutomationTabItem,
    AutomationText,
    AutomationToolBar,
    AutomationToolTip,
    AutomationTree,
    AutomationTreeItem,
    AutomationCustom,
    AutomationGroup,
    AutomationThumb,
    AutomationDataGrid,
    AutomationDataItem,
    AutomationDocument,
    AutomationSplitButton,
    AutomationWindow,
    AutomationPane,
    AutomationHeader,
    AutomationHeaderItem,
    AutomationTable,
    AutomationTitleBar,
    AutomationSeparator,
    AutomationExpander,
    AutomationScrollViewer,
};
enum AvnLandmarkType
{
    LandmarkNone = -1,
    LandmarkBanner,
    LandmarkComplementary,
    LandmarkContentInfo,
    LandmarkRegion,
    LandmarkForm,
    LandmarkMain,
    LandmarkNavigation,
    LandmarkSearch,
};
enum AvnWindowTransparencyMode
{
    Opaque,
    Transparent,
    Blur,
};
enum AvnPlatformThemeVariant
{
    Light,
    Dark,
    HighContrastLight,
    HighContrastDark,
};
enum AvnPointerDeviceType
{
    Mouse,
    Pen,
};
enum AvnLiveSetting
{
    LiveSettingOff,
    LiveSettingPolite,
    LiveSettingAssertive,
};
struct AvnSize
{
    double Width;
    double Height;
};
struct AvnPixelSize
{
    int Width;
    int Height;
};
struct AvnRect
{
    double X;
    double Y;
    double Width;
    double Height;
};
struct AvnVector
{
    double X;
    double Y;
};
struct AvnPoint
{
    double X;
    double Y;
};
struct AvnScreen
{
    AvnRect Bounds;
    AvnRect WorkingArea;
    float Scaling;
    bool IsPrimary;
    AvnScreenOrientation Orientation;
};
struct AvnFramebuffer
{
    void* Data;
    int Width;
    int Height;
    int Stride;
    AvnVector Dpi;
    AvnPixelFormat PixelFormat;
};
struct AvnColor
{
    unsigned char Alpha;
    unsigned char Red;
    unsigned char Green;
    unsigned char Blue;
};
COMINTERFACE(IAvaloniaNativeFactory, 809c652e, 7396, 11d2, 97, 71, 00, a0, c9, b4, d5, 0c) : IUnknown
{
    virtual HRESULT Initialize (
        IAvnGCHandleDeallocatorCallback* deallocator, 
        IAvnApplicationEvents* appCb, 
        IAvnDispatcher* dispatcher
    ) = 0;
    virtual IAvnMacOptions* GetMacOptions () = 0;
    virtual HRESULT CreateTopLevel (
        IAvnTopLevelEvents* cb, 
        IAvnTopLevel** ppv
    ) = 0;
    virtual HRESULT CreateWindow (
        IAvnWindowEvents* cb, 
        IAvnWindow** ppv
    ) = 0;
    virtual HRESULT CreatePopup (
        IAvnWindowEvents* cb, 
        IAvnPopup** ppv
    ) = 0;
    virtual HRESULT CreatePlatformThreadingInterface (
        IAvnPlatformThreadingInterface** ppv
    ) = 0;
    virtual HRESULT CreateStorageProvider (
        IAvnStorageProvider** ppv
    ) = 0;
    virtual HRESULT CreateScreens (
        IAvnScreenEvents* cb, 
        IAvnScreens** ppv
    ) = 0;
    virtual HRESULT CreateClipboard (
        IAvnClipboard** ppv
    ) = 0;
    virtual HRESULT CreateCursorFactory (
        IAvnCursorFactory** ppv
    ) = 0;
    virtual HRESULT ObtainGlDisplay (
        IAvnGlDisplay** ppv
    ) = 0;
    virtual HRESULT ObtainMetalDisplay (
        IAvnMetalDisplay** ppv
    ) = 0;
    virtual HRESULT SetAppMenu (
        IAvnMenu* menu
    ) = 0;
    virtual HRESULT SetServicesMenu (
        IAvnMenu* menu
    ) = 0;
    virtual HRESULT CreateMenu (
        IAvnMenuEvents* cb, 
        IAvnMenu** ppv
    ) = 0;
    virtual HRESULT CreateMenuItem (
        IAvnMenuItem** ppv
    ) = 0;
    virtual HRESULT CreateMenuItemSeparator (
        IAvnMenuItem** ppv
    ) = 0;
    virtual HRESULT CreateTrayIcon (
        IAvnTrayIcon** ppv
    ) = 0;
    virtual HRESULT CreateApplicationCommands (
        IAvnApplicationCommands** ppv
    ) = 0;
    virtual HRESULT CreatePlatformSettings (
        IAvnPlatformSettings** ppv
    ) = 0;
    virtual HRESULT CreatePlatformBehaviorInhibition (
        IAvnPlatformBehaviorInhibition** ppv
    ) = 0;
    virtual HRESULT CreatePlatformRenderTimer (
        IAvnPlatformRenderTimer** ppv
    ) = 0;
    virtual HRESULT ImportMTLSharedEvent (
        void* idMtlSharedEvent, 
        IAvnMTLSharedEvent** ppv
    ) = 0;
    virtual HRESULT CreateMemoryManagementHelper (
        IAvnNativeObjectsMemoryManagement** ppv
    ) = 0;
    virtual HRESULT SetDockMenu (
        IAvnMenu* menu
    ) = 0;
};
COMINTERFACE(IAvnString, 233e094f, 9b9f, 44a3, 9a, 6e, 69, 48, bb, dd, 9f, b1) : IUnknown
{
    virtual HRESULT Pointer (
        void** retOut
    ) = 0;
    virtual HRESULT Length (
        int* ret
    ) = 0;
};
COMINTERFACE(IAvnTopLevel, e8cccd3e, e6dc, 430a, a0, b9, 2c, e7, d7, 92, 2d, e6) : IUnknown
{
    virtual HRESULT GetClientSize (
        AvnSize* ret
    ) = 0;
    virtual HRESULT GetScaling (
        double* ret
    ) = 0;
    virtual HRESULT Invalidate () = 0;
    virtual HRESULT PointToClient (
        AvnPoint point, 
        AvnPoint* ret
    ) = 0;
    virtual HRESULT PointToScreen (
        AvnPoint point, 
        AvnPoint* ret
    ) = 0;
    virtual HRESULT SetCursor (
        IAvnCursor* cursor
    ) = 0;
    virtual HRESULT CreateGlRenderTarget (
        IAvnGlContext* context, 
        IAvnGlSurfaceRenderTarget** ret
    ) = 0;
    virtual HRESULT CreateSoftwareRenderTarget (
        IAvnSoftwareRenderTarget** ret
    ) = 0;
    virtual HRESULT CreateMetalRenderTarget (
        IAvnMetalDevice* device, 
        IAvnMetalRenderTarget** ret
    ) = 0;
    virtual HRESULT ObtainNSViewHandle (
        void** retOut
    ) = 0;
    virtual HRESULT ObtainNSViewHandleRetained (
        void** retOut
    ) = 0;
    virtual HRESULT CreateNativeControlHost (
        IAvnNativeControlHost** retOut
    ) = 0;
    virtual HRESULT GetInputMethod (
        IAvnTextInputMethod** ppv
    ) = 0;
    virtual HRESULT SetTransparencyMode (
        AvnWindowTransparencyMode mode
    ) = 0;
    virtual HRESULT GetCurrentDisplayId (
        unsigned int* ret
    ) = 0;
    virtual HRESULT BeginDragAndDropOperation (
        AvnDragDropEffects effects, 
        AvnPoint point, 
        IAvnClipboardDataSource* source, 
        IAvnDndResultCallback* callback, 
        void* sourceHandle
    ) = 0;
};
COMINTERFACE(IAvnWindowBase, e5aca675, 02b7, 4129, aa, 79, d6, e4, 17, 21, 0b, da) : virtual IAvnTopLevel
{
    virtual HRESULT GetFrameSize (
        AvnSize* result
    ) = 0;
    virtual HRESULT SetFrameThemeVariant (
        AvnPlatformThemeVariant mode
    ) = 0;
    virtual HRESULT SetParent (
        IAvnWindowBase* parent
    ) = 0;
    virtual HRESULT Show (
        bool activate, 
        bool isDialog
    ) = 0;
    virtual HRESULT Hide () = 0;
    virtual HRESULT Close () = 0;
    virtual HRESULT Activate () = 0;
    virtual HRESULT SetMinMaxSize (
        AvnSize minSize, 
        AvnSize maxSize
    ) = 0;
    virtual HRESULT Resize (
        double width, 
        double height, 
        AvnPlatformResizeReason reason
    ) = 0;
    virtual HRESULT BeginMoveDrag () = 0;
    virtual HRESULT BeginResizeDrag (
        AvnWindowEdge edge
    ) = 0;
    virtual HRESULT GetPosition (
        AvnPoint* ret
    ) = 0;
    virtual HRESULT SetPosition (
        AvnPoint point
    ) = 0;
    virtual HRESULT SetTopMost (
        bool value
    ) = 0;
    virtual HRESULT SetMainMenu (
        IAvnMenu* menu
    ) = 0;
    virtual HRESULT ObtainNSWindowHandle (
        void** retOut
    ) = 0;
    virtual HRESULT ObtainNSWindowHandleRetained (
        void** retOut
    ) = 0;
};
COMINTERFACE(IAvnPopup, 83e588f3, 6981, 4e48, 9e, a0, e1, e5, 69, f7, 9a, 91) : virtual IAvnWindowBase
{
};
COMINTERFACE(IAvnWindow, cab661de, 49d6, 4ead, b5, 9c, ea, c9, b2, b6, c2, 8d) : virtual IAvnWindowBase
{
    virtual HRESULT SetEnabled (
        bool enable
    ) = 0;
    virtual HRESULT SetCanResize (
        bool value
    ) = 0;
    virtual HRESULT SetCanMinimize (
        bool value
    ) = 0;
    virtual HRESULT SetCanMaximize (
        bool value
    ) = 0;
    virtual HRESULT SetDecorations (
        SystemDecorations value
    ) = 0;
    virtual HRESULT SetTitle (
        char* utf8Title
    ) = 0;
    virtual HRESULT SetTitleBarColor (
        AvnColor color
    ) = 0;
    virtual HRESULT SetWindowState (
        AvnWindowState state
    ) = 0;
    virtual HRESULT GetWindowState (
        AvnWindowState* ret
    ) = 0;
    virtual HRESULT TakeFocusFromChildren () = 0;
    virtual HRESULT SetExtendClientArea (
        bool enable
    ) = 0;
    virtual HRESULT GetExtendTitleBarHeight (
        double* ret
    ) = 0;
    virtual HRESULT SetExtendTitleBarHeight (
        double value
    ) = 0;
    virtual HRESULT GetWindowZOrder (
        long* ret
    ) = 0;
};
COMINTERFACE(IAvnTopLevelEvents, fda9c1b3, 69e0, 43d7, 94, 59, 8c, c9, 7c, b4, 1f, 6a) : IUnknown
{
    virtual void Closed () = 0;
    virtual HRESULT Paint () = 0;
    virtual void Resized (
        const AvnSize& size, 
        AvnPlatformResizeReason reason
    ) = 0;
    virtual void RawMouseEvent (
        AvnRawMouseEventType type, 
        AvnPointerDeviceType deviceType, 
        u_int64_t timeStamp, 
        AvnInputModifiers modifiers, 
        AvnPoint point, 
        AvnVector delta, 
        float pressure, 
        float xTilt, 
        float yTilt
    ) = 0;
    virtual bool RawKeyEvent (
        AvnRawKeyEventType type, 
        u_int64_t timeStamp, 
        AvnInputModifiers modifiers, 
        AvnKey key, 
        AvnPhysicalKey physicalKey, 
        const char* keySymbol
    ) = 0;
    virtual bool RawTextInputEvent (
        u_int64_t timeStamp, 
        const char* text
    ) = 0;
    virtual void ScalingChanged (
        double scaling
    ) = 0;
    virtual void RunRenderPriorityJobs () = 0;
    virtual void LostFocus () = 0;
    virtual IAvnAutomationPeer* GetAutomationPeer () = 0;
    virtual AvnDragDropEffects DragEvent (
        AvnDragEventType type, 
        AvnPoint position, 
        AvnInputModifiers modifiers, 
        AvnDragDropEffects effects, 
        IAvnClipboard* clipboard, 
        void* dataTransferHandle
    ) = 0;
};
COMINTERFACE(IAvnWindowBaseEvents, 939b6599, 40a8, 4710, a4, c8, 5d, 72, d8, f1, 74, fb) : IAvnTopLevelEvents
{
    virtual void Activated () = 0;
    virtual void Deactivated () = 0;
    virtual void PositionChanged (
        AvnPoint position
    ) = 0;
};
COMINTERFACE(IAvnWindowEvents, 1ae178ee, 1fcc, 447f, b6, dd, b7, bb, 72, 7f, 93, 4c) : IAvnWindowBaseEvents
{
    virtual bool Closing () = 0;
    virtual void WindowStateChanged (
        AvnWindowState state
    ) = 0;
    virtual void GotInputWhenDisabled () = 0;
};
COMINTERFACE(IAvnTextInputMethodClient, f2079145, a2d9, 42b8, a8, 5e, 27, 32, e3, c2, b0, 55) : IUnknown
{
    virtual void SetPreeditText (
        char* preeditText
    ) = 0;
    virtual void SelectInSurroundingText (
        int start, 
        int end
    ) = 0;
};
COMINTERFACE(IAvnTextInputMethod, 1382a29f, e260, 4c7a, b8, 3f, c9, 9f, c7, 2e, 27, c2) : IUnknown
{
    virtual HRESULT SetClient (
        IAvnTextInputMethodClient* client
    ) = 0;
    virtual void Reset () = 0;
    virtual void SetCursorRect (
        AvnRect rect
    ) = 0;
    virtual void SetSurroundingText (
        char* text, 
        int start, 
        int end
    ) = 0;
    virtual void SetSelectionInSurroundingText (
        int start, 
        int end
    ) = 0;
};
COMINTERFACE(IAvnMacOptions, e34ae0f8, 18b4, 48a3, b0, 9d, 2e, 6b, 19, a3, cf, 5e) : IUnknown
{
    virtual HRESULT SetShowInDock (
        int show
    ) = 0;
    virtual HRESULT SetApplicationTitle (
        char* utf8string
    ) = 0;
    virtual HRESULT SetDisableSetProcessName (
        int disable
    ) = 0;
    virtual HRESULT SetDisableAppDelegate (
        int disable
    ) = 0;
};
COMINTERFACE(IAvnActionCallback, 04c1b049, 1f43, 418a, 91, 59, ca, e6, 27, ec, 13, 67) : IUnknown
{
    virtual void Run () = 0;
};
COMINTERFACE(IAvnPlatformThreadingInterfaceEvents, 6df4d2db, 0b80, 4f59, ad, 88, 0b, aa, 5e, 21, eb, 14) : IUnknown
{
    virtual void Signaled () = 0;
    virtual void Timer () = 0;
    virtual void ReadyForBackgroundProcessing () = 0;
};
COMINTERFACE(IAvnLoopCancellation, 97330f88, c22b, 4a8e, a1, 30, 20, 15, 20, 09, 1b, 01) : IUnknown
{
    virtual void Cancel () = 0;
};
COMINTERFACE(IAvnPlatformThreadingInterface, fbc06f3d, 7860, 42df, 83, fd, 53, c4, b0, 2d, d9, c3) : IUnknown
{
    virtual bool GetCurrentThreadIsLoopThread () = 0;
    virtual void SetEvents (
        IAvnPlatformThreadingInterfaceEvents* cb
    ) = 0;
    virtual IAvnLoopCancellation* CreateLoopCancellation () = 0;
    virtual void RunLoop (
        IAvnLoopCancellation* cancel
    ) = 0;
    virtual void Signal () = 0;
    virtual void UpdateTimer (
        int ms
    ) = 0;
    virtual void RequestBackgroundProcessing () = 0;
};
COMINTERFACE(IAvnSystemDialogEvents, 6c621a6e, e4c1, 4ae3, 97, 49, 83, ee, ef, fa, 09, b6) : IUnknown
{
    virtual void OnCompleted (
        IAvnStringArray* array
    ) = 0;
    virtual void OnCompletedWithFilter (
        IAvnStringArray* array, 
        int selectedFilterIndex
    ) = 0;
};
COMINTERFACE(IAvnStorageProvider, 4d7a47db, a944, 4061, ab, e7, 62, cb, 6a, a0, ff, d5) : IUnknown
{
    virtual void SelectFolderDialog (
        IAvnTopLevel* parentTopLevel, 
        IAvnSystemDialogEvents* events, 
        bool allowMultiple, 
        const char* title, 
        const char* initialPath
    ) = 0;
    virtual void OpenFileDialog (
        IAvnTopLevel* parentTopLevel, 
        IAvnSystemDialogEvents* events, 
        bool allowMultiple, 
        const char* title, 
        const char* initialDirectory, 
        const char* initialFile, 
        IAvnFilePickerFileTypes* filters
    ) = 0;
    virtual void SaveFileDialog (
        IAvnTopLevel* parentTopLevel, 
        IAvnSystemDialogEvents* events, 
        const char* title, 
        const char* initialDirectory, 
        const char* initialFile, 
        IAvnFilePickerFileTypes* filters
    ) = 0;
    virtual HRESULT SaveBookmarkToBytes (
        IAvnString* fileUri, 
        void** err, 
        IAvnString** ppv
    ) = 0;
    virtual HRESULT ReadBookmarkFromBytes (
        void* ptr, 
        int len, 
        IAvnString** ppv
    ) = 0;
    virtual void ReleaseBookmark (
        IAvnString* fileUri
    ) = 0;
    virtual bool OpenSecurityScope (
        IAvnString* fileUri
    ) = 0;
    virtual void CloseSecurityScope (
        IAvnString* fileUri
    ) = 0;
    virtual HRESULT TryResolveFileReferenceUri (
        IAvnString* fileUri, 
        IAvnString** ret
    ) = 0;
};
COMINTERFACE(IAvnFilePickerFileTypes, 4d7ab7db, a111, 406f, ab, eb, 11, cb, 6a, a0, 33, d5) : IUnknown
{
    virtual int GetCount () = 0;
    virtual bool IsDefaultType (
        int index
    ) = 0;
    virtual bool IsAnyType (
        int index
    ) = 0;
    virtual IAvnString* GetName (
        int index
    ) = 0;
    virtual HRESULT GetPatterns (
        int index, 
        IAvnStringArray** ppv
    ) = 0;
    virtual HRESULT GetExtensions (
        int index, 
        IAvnStringArray** ppv
    ) = 0;
    virtual HRESULT GetMimeTypes (
        int index, 
        IAvnStringArray** ppv
    ) = 0;
    virtual HRESULT GetAppleUniformTypeIdentifiers (
        int index, 
        IAvnStringArray** ppv
    ) = 0;
};
COMINTERFACE(IAvnScreenEvents, 424b1bd4, a111, 4987, bf, d0, 9d, 64, 21, 54, b1, b3) : IUnknown
{
    virtual HRESULT OnChanged () = 0;
};
COMINTERFACE(IAvnScreens, 9a52bc7a, d8c7, 4230, 8d, 34, 70, 4a, 0b, 70, a9, 33) : IUnknown
{
    virtual HRESULT GetScreenIds (
        unsigned int* ptrFirstResult, 
        int* ret
    ) = 0;
    virtual HRESULT GetScreen (
        unsigned int screenId, 
        void** localizedName, 
        AvnScreen* ret
    ) = 0;
};
COMINTERFACE(IAvnClipboard, 792b1bd4, 76cc, 46ea, bf, d0, 9d, 64, 21, 54, b1, b3) : IUnknown
{
    virtual HRESULT GetFormats (
        int64_t changeCount, 
        IAvnStringArray** ret
    ) = 0;
    virtual HRESULT GetItemCount (
        int64_t changeCount, 
        int* ret
    ) = 0;
    virtual HRESULT GetItemFormats (
        int index, 
        int64_t changeCount, 
        IAvnStringArray** ret
    ) = 0;
    virtual HRESULT GetItemValueAsString (
        int index, 
        int64_t changeCount, 
        const char* format, 
        IAvnString** ret
    ) = 0;
    virtual HRESULT GetItemValueAsBytes (
        int index, 
        int64_t changeCount, 
        const char* format, 
        IAvnString** ret
    ) = 0;
    virtual HRESULT Clear (
        int64_t* ret
    ) = 0;
    virtual HRESULT GetChangeCount (
        int64_t* ret
    ) = 0;
    virtual HRESULT SetData (
        IAvnClipboardDataSource* dataSource
    ) = 0;
    virtual bool IsTextFormat (
        const char* format
    ) = 0;
};
COMINTERFACE(IAvnClipboardDataSource, 10b39f02, efcb, 428b, be, e5, a0, b0, 12, c1, fb, 7d) : IUnknown
{
    virtual int GetItemCount () = 0;
    virtual HRESULT GetItem (
        int index, 
        IAvnClipboardDataItem** ppv
    ) = 0;
};
COMINTERFACE(IAvnClipboardDataItem, e40f36d9, 69f4, 45fd, 9c, a2, 6e, 64, e8, 0f, eb, 6d) : IUnknown
{
    virtual HRESULT ProvideFormats (
        IAvnStringArray** ppv
    ) = 0;
    virtual HRESULT GetValue (
        const char* format, 
        IAvnClipboardDataValue** ppv
    ) = 0;
};
COMINTERFACE(IAvnClipboardDataValue, e97f24f6, 1c84, 4d95, 8f, fe, 5b, 2c, 72, e0, 16, ed) : IUnknown
{
    virtual bool IsString () = 0;
    virtual IAvnString* AsString () = 0;
    virtual long GetByteLength () = 0;
    virtual void CopyBytesTo (
        void* buffer
    ) = 0;
};
COMINTERFACE(IAvnCursor, 3f998545, f027, 4d4d, bd, 2a, 1a, 80, 92, 6d, 98, 4e) : IUnknown
{
};
COMINTERFACE(IAvnCursorFactory, 51ecfb12, c427, 4757, a2, c9, 15, 96, bf, ce, 53, ef) : IUnknown
{
    virtual HRESULT GetCursor (
        AvnStandardCursorType cursorType, 
        IAvnCursor** retOut
    ) = 0;
    virtual HRESULT CreateCustomCursor (
        void* bitmapData, 
        size_t length, 
        AvnPixelSize hotPixel, 
        IAvnCursor** retOut
    ) = 0;
};
COMINTERFACE(IAvnSoftwareRenderTarget, 931062d2, 5bc8, 4062, 85, 88, 83, dd, 8d, eb, 99, c2) : IUnknown
{
    virtual HRESULT SetFrame (
        AvnFramebuffer* fb
    ) = 0;
};
COMINTERFACE(IAvnGlDisplay, 60452465, 8616, 40af, bc, 00, 04, 2e, 69, 82, 8c, e7) : IUnknown
{
    virtual HRESULT CreateContext (
        IAvnGlContext* share, 
        IAvnGlContext** ppv
    ) = 0;
    virtual void LegacyClearCurrentContext () = 0;
    virtual HRESULT WrapContext (
        void* native, 
        IAvnGlContext** ppv
    ) = 0;
    virtual void* GetProcAddress (
        char* proc
    ) = 0;
};
COMINTERFACE(IAvnGlContext, 78c5711e, 2a98, 40d2, ba, c4, 0c, c9, a4, 9d, c4, f3) : IUnknown
{
    virtual HRESULT MakeCurrent (
        IUnknown** ppv
    ) = 0;
    virtual HRESULT LegacyMakeCurrent () = 0;
    virtual int GetSampleCount () = 0;
    virtual int GetStencilSize () = 0;
    virtual void* GetNativeHandle () = 0;
    virtual int texImageIOSurface2D (
        int target, 
        int internal_format, 
        int width, 
        int height, 
        int format, 
        int type, 
        void* ioSurface, 
        int plane
    ) = 0;
    virtual bool GetIOKitRegistryId (
        uint64_t* value
    ) = 0;
};
COMINTERFACE(IAvnGlSurfaceRenderTarget, 931062d2, 5bc8, 4062, 85, 88, 83, dd, 8d, eb, 99, c2) : IUnknown
{
    virtual HRESULT BeginDrawing (
        IAvnGlSurfaceRenderingSession** ret
    ) = 0;
};
COMINTERFACE(IAvnGlSurfaceRenderingSession, e625b406, f04c, 484e, 94, 6a, 4a, bd, 2c, 60, 15, ad) : IUnknown
{
    virtual HRESULT GetPixelSize (
        AvnPixelSize* ret
    ) = 0;
    virtual HRESULT GetScaling (
        double* ret
    ) = 0;
};
COMINTERFACE(IAvnMetalDisplay, da291767, 4db3, 4598, 89, 3d, 09, ec, aa, 23, 89, 3f) : IUnknown
{
    virtual HRESULT CreateDevice (
        IAvnMetalDevice** ret
    ) = 0;
};
COMINTERFACE(IAvnMetalDevice, 969fa914, b74a, 4c9f, 87, 25, 51, 60, dc, 63, 57, 9e) : IUnknown
{
    virtual void* GetDevice () = 0;
    virtual void* GetQueue () = 0;
    virtual bool GetIOKitRegistryId (
        uint64_t* value
    ) = 0;
    virtual HRESULT ImportIOSurface (
        void* handle, 
        AvnPixelFormat pixelFormat, 
        IAvnMetalTexture** ppv
    ) = 0;
    virtual HRESULT ImportSharedEvent (
        void* mtlSharedEventInstance, 
        IAvnMTLSharedEvent** ppv
    ) = 0;
    virtual HRESULT SubmitWait (
        IAvnMTLSharedEvent* ev, 
        uint64_t value
    ) = 0;
    virtual HRESULT SubmitSignal (
        IAvnMTLSharedEvent* ev, 
        uint64_t value
    ) = 0;
};
COMINTERFACE(IAvnMetalRenderTarget, f1306b71, eca0, 426e, 87, 00, 10, 51, 92, 69, 3b, 1a) : IUnknown
{
    virtual HRESULT BeginDrawing (
        IAvnMetalRenderingSession** ret
    ) = 0;
};
COMINTERFACE(IAvnMTLSharedEvent, a1f4fcde, 9152, 48bd, bf, 8a, b1, b6, 51, 13, 4a, 69) : IUnknown
{
    virtual void* GetNativeHandle () = 0;
    virtual bool Wait (
        uint64_t value, 
        uint64_t timeoutMS
    ) = 0;
    virtual void SetSignaledValue (
        uint64_t value
    ) = 0;
    virtual uint64_t GetSignaledValue () = 0;
};
COMINTERFACE(IAvnMetalTexture, 722aad20, a87b, 4ce5, b5, 0f, f0, 5c, fa, 4c, da, 39) : IUnknown
{
    virtual void* GetNativeHandle () = 0;
    virtual int GetWidth () = 0;
    virtual int GetHeight () = 0;
    virtual int GetSampleCount () = 0;
};
COMINTERFACE(IAvnNativeObjectsMemoryManagement, 74027aa2, 5262, 45a5, a7, 4a, 5a, 53, 37, 3d, cc, 17) : IUnknown
{
    virtual void RetainNSObject (
        void* obj
    ) = 0;
    virtual void ReleaseNSObject (
        void* obj
    ) = 0;
    virtual uint64_t GetRetainCountForNSObject (
        void* obj
    ) = 0;
    virtual void RetainCFObject (
        void* obj
    ) = 0;
    virtual void ReleaseCFObject (
        void* obj
    ) = 0;
    virtual int64_t GetRetainCountForCFObject (
        void* obj
    ) = 0;
};
COMINTERFACE(IAvnMetalRenderingSession, e625b406, f04c, 484e, 94, 6a, 4a, bd, 2c, 60, 15, ad) : IUnknown
{
    virtual HRESULT GetPixelSize (
        AvnPixelSize* ret
    ) = 0;
    virtual double GetScaling () = 0;
    virtual void* GetTexture () = 0;
};
COMINTERFACE(IAvnTrayIcon, 60992d19, 38f0, 4141, a0, a9, 76, ac, 30, 38, 01, f3) : IUnknown
{
    virtual HRESULT SetIcon (
        void* data, 
        size_t length
    ) = 0;
    virtual HRESULT SetMenu (
        IAvnMenu* menu
    ) = 0;
    virtual HRESULT SetIsVisible (
        bool isVisible
    ) = 0;
    virtual HRESULT SetToolTipText (
        char* text
    ) = 0;
    virtual HRESULT SetIsTemplateIcon (
        bool text
    ) = 0;
};
COMINTERFACE(IAvnMenu, a7724dc1, cf6b, 4fa8, 9d, 23, 22, 8b, f2, 59, 3e, dc) : IUnknown
{
    virtual HRESULT InsertItem (
        int index, 
        IAvnMenuItem* item
    ) = 0;
    virtual HRESULT RemoveItem (
        IAvnMenuItem* item
    ) = 0;
    virtual HRESULT SetTitle (
        char* utf8String
    ) = 0;
    virtual HRESULT Clear () = 0;
};
COMINTERFACE(IAvnPredicateCallback, 59e0586d, bd1c, 4b85, 98, 82, 80, d4, 48, b0, fe, d9) : IUnknown
{
    virtual bool Evaluate () = 0;
};
COMINTERFACE(IAvnMenuItem, f890219a, 1720, 4cd5, 9a, 26, cd, 95, fc, cb, f5, 3c) : IUnknown
{
    virtual HRESULT SetSubMenu (
        IAvnMenu* menu
    ) = 0;
    virtual HRESULT SetTitle (
        char* utf8String
    ) = 0;
    virtual HRESULT SetToolTip (
        char* utf8String
    ) = 0;
    virtual HRESULT SetGesture (
        AvnKey key, 
        AvnInputModifiers modifiers
    ) = 0;
    virtual HRESULT SetAction (
        IAvnPredicateCallback* predicate, 
        IAvnActionCallback* callback
    ) = 0;
    virtual HRESULT SetIsChecked (
        bool isChecked
    ) = 0;
    virtual HRESULT SetIsVisible (
        bool isVisible
    ) = 0;
    virtual HRESULT SetToggleType (
        AvnMenuItemToggleType toggleType
    ) = 0;
    virtual HRESULT SetIcon (
        void* data, 
        size_t length
    ) = 0;
};
COMINTERFACE(IAvnMenuEvents, 0af7df53, 7632, 42f4, a6, 50, 09, 92, c3, 61, b4, 77) : IUnknown
{
    virtual void NeedsUpdate () = 0;
    virtual void Opening () = 0;
    virtual void Closed () = 0;
};
COMINTERFACE(IAvnStringArray, 5142bb41, 66ab, 49e7, bb, 37, cd, 07, 9c, 00, 0f, 27) : IUnknown
{
    virtual unsigned int GetCount () = 0;
    virtual HRESULT Get (
        unsigned int index, 
        IAvnString** ppv
    ) = 0;
};
COMINTERFACE(IAvnDndResultCallback, a13d2382, 3b3a, 4d1c, 9b, 27, 8f, 34, 65, 3d, 3f, 01) : IUnknown
{
    virtual void OnDragAndDropComplete (
        AvnDragDropEffects effecct
    ) = 0;
};
COMINTERFACE(IAvnGCHandleDeallocatorCallback, f07c608e, 52e9, 422d, 83, 6e, c7, 0f, 6e, 9b, 80, f5) : IUnknown
{
    virtual void FreeGCHandle (
        void* handle
    ) = 0;
};
COMINTERFACE(IAvnDispatcher, 96688589, 5dc7, 41ec, 9c, e3, d4, 81, 94, 24, 54, ee) : IUnknown
{
    virtual void Post (
        IAvnActionCallback* cb
    ) = 0;
};
COMINTERFACE(IAvnNativeControlHost, 91c7f677, f26b, 4ff3, 93, cc, cf, 15, aa, 96, 6f, fa) : IUnknown
{
    virtual HRESULT CreateDefaultChild (
        void* parent, 
        void** retOut
    ) = 0;
    virtual IAvnNativeControlHostTopLevelAttachment* CreateAttachment () = 0;
    virtual void DestroyDefaultChild (
        void* child
    ) = 0;
};
COMINTERFACE(IAvnNativeControlHostTopLevelAttachment, 14a9e164, 1aae, 4271, bb, 78, 7b, 52, 30, 99, 9b, 52) : IUnknown
{
    virtual void* GetParentHandle () = 0;
    virtual HRESULT InitializeWithChildHandle (
        void* child
    ) = 0;
    virtual HRESULT AttachTo (
        IAvnNativeControlHost* host
    ) = 0;
    virtual void ShowInBounds (
        float x, 
        float y, 
        float width, 
        float height
    ) = 0;
    virtual void HideWithSize (
        float width, 
        float height
    ) = 0;
    virtual void ReleaseChild () = 0;
};
COMINTERFACE(IAvnApplicationEvents, 6575b5af, f27a, 4609, 86, 6c, f1, f0, 14, c2, 0f, 79) : IUnknown
{
    virtual void FilesOpened (
        IAvnStringArray* args
    ) = 0;
    virtual void UrlsOpened (
        IAvnStringArray* urls
    ) = 0;
    virtual bool TryShutdown () = 0;
    virtual void OnReopen () = 0;
    virtual void OnHide () = 0;
    virtual void OnUnhide () = 0;
    virtual void OnActivate () = 0;
    virtual void OnDeactivate () = 0;
};
COMINTERFACE(IAvnApplicationCommands, b4284791, 055b, 4313, 8c, 2e, 50, f0, a8, c7, 2c, e9) : IUnknown
{
    virtual HRESULT UnhideApp () = 0;
    virtual HRESULT HideApp () = 0;
    virtual HRESULT ShowAll () = 0;
    virtual HRESULT HideOthers () = 0;
};
COMINTERFACE(IAvnAutomationPeer, b87016f3, 7eec, 41de, b3, 85, 07, 84, 4c, 26, 8d, c4) : IUnknown
{
    virtual IAvnAutomationNode* GetNode () = 0;
    virtual void SetNode (
        IAvnAutomationNode* node
    ) = 0;
    virtual IAvnString* GetAcceleratorKey () = 0;
    virtual IAvnString* GetAccessKey () = 0;
    virtual AvnAutomationControlType GetAutomationControlType () = 0;
    virtual IAvnString* GetAutomationId () = 0;
    virtual AvnRect GetBoundingRectangle () = 0;
    virtual IAvnAutomationPeerArray* GetChildren () = 0;
    virtual IAvnString* GetClassName () = 0;
    virtual IAvnAutomationPeer* GetLabeledBy () = 0;
    virtual IAvnString* GetName () = 0;
    virtual IAvnAutomationPeer* GetParent () = 0;
    virtual IAvnAutomationPeer* GetTemplatedParent () = 0;
    virtual IAvnAutomationPeer* GetVisualRoot () = 0;
    virtual bool HasKeyboardFocus () = 0;
    virtual bool IsContentElement () = 0;
    virtual bool IsControlElement () = 0;
    virtual bool IsEnabled () = 0;
    virtual bool IsKeyboardFocusable () = 0;
    virtual void SetFocus () = 0;
    virtual bool ShowContextMenu () = 0;
    virtual void BringIntoView () = 0;
    virtual IAvnAutomationPeer* GetRootPeer () = 0;
    virtual bool IsInteropPeer () = 0;
    virtual void* InteropPeer_GetNativeControlHandle () = 0;
    virtual bool IsRootProvider () = 0;
    virtual IAvnWindowBase* RootProvider_GetWindow () = 0;
    virtual IAvnAutomationPeer* RootProvider_GetFocus () = 0;
    virtual IAvnAutomationPeer* RootProvider_GetPeerFromPoint (
        AvnPoint point
    ) = 0;
    virtual bool IsEmbeddedRootProvider () = 0;
    virtual IAvnAutomationPeer* EmbeddedRootProvider_GetFocus () = 0;
    virtual IAvnAutomationPeer* EmbeddedRootProvider_GetPeerFromPoint (
        AvnPoint point
    ) = 0;
    virtual bool IsExpandCollapseProvider () = 0;
    virtual bool ExpandCollapseProvider_GetIsExpanded () = 0;
    virtual bool ExpandCollapseProvider_GetShowsMenu () = 0;
    virtual void ExpandCollapseProvider_Expand () = 0;
    virtual void ExpandCollapseProvider_Collapse () = 0;
    virtual bool IsInvokeProvider () = 0;
    virtual void InvokeProvider_Invoke () = 0;
    virtual bool IsRangeValueProvider () = 0;
    virtual double RangeValueProvider_GetValue () = 0;
    virtual double RangeValueProvider_GetMinimum () = 0;
    virtual double RangeValueProvider_GetMaximum () = 0;
    virtual double RangeValueProvider_GetSmallChange () = 0;
    virtual double RangeValueProvider_GetLargeChange () = 0;
    virtual void RangeValueProvider_SetValue (
        double value
    ) = 0;
    virtual bool RangeValueProvider_IsReadOnly () = 0;
    virtual bool IsSelectionItemProvider () = 0;
    virtual bool SelectionItemProvider_IsSelected () = 0;
    virtual void SelectionItemProvider_Select () = 0;
    virtual void SelectionItemProvider_AddToSelection () = 0;
    virtual void SelectionItemProvider_RemoveFromSelection () = 0;
    virtual IAvnAutomationPeer* ScrollProvider_GetHorizontalScrollBar () = 0;
    virtual IAvnAutomationPeer* ScrollProvider_GetVerticalScrollBar () = 0;
    virtual bool IsToggleProvider () = 0;
    virtual int ToggleProvider_GetToggleState () = 0;
    virtual void ToggleProvider_Toggle () = 0;
    virtual bool IsValueProvider () = 0;
    virtual IAvnString* ValueProvider_GetValue () = 0;
    virtual void ValueProvider_SetValue (
        const char* value
    ) = 0;
    virtual bool ValueProvider_IsReadOnly () = 0;
    virtual IAvnString* GetHelpText () = 0;
    virtual IAvnString* GetPlaceholderText () = 0;
    virtual AvnLandmarkType GetLandmarkType () = 0;
    virtual int GetHeadingLevel () = 0;
    virtual AvnLiveSetting GetLiveSetting () = 0;
};
COMINTERFACE(IAvnAutomationPeerArray, b00af5da, 78af, 4b33, bf, ff, 4c, e1, 3a, 62, 39, a9) : IUnknown
{
    virtual unsigned int GetCount () = 0;
    virtual HRESULT Get (
        unsigned int index, 
        IAvnAutomationPeer** ppv
    ) = 0;
};
COMINTERFACE(IAvnAutomationNode, 004dc40b, e435, 49dc, ba, c5, 62, 72, ee, 35, 38, 2a) : IUnknown
{
    virtual void Dispose () = 0;
    virtual void ChildrenChanged () = 0;
    virtual void PropertyChanged (
        AvnAutomationProperty property
    ) = 0;
    virtual void FocusChanged () = 0;
};
COMINTERFACE(IAvnPlatformSettings, d1f009cc, 9d2d, 493b, 84, 5d, 90, d2, c1, 04, ba, ae) : IUnknown
{
    virtual AvnPlatformThemeVariant GetPlatformTheme () = 0;
    virtual unsigned int GetAccentColor () = 0;
    virtual void RegisterColorsChange (
        IAvnActionCallback* callback
    ) = 0;
};
COMINTERFACE(IAvnPlatformBehaviorInhibition, 12edf00d, 5803, 4d3f, 99, 47, b4, 84, 0e, 5e, 93, 72) : IUnknown
{
    virtual void SetInhibitAppSleep (
        bool inhibitAppSleep, 
        char* reason
    ) = 0;
};
COMINTERFACE(IAvnPlatformRenderTimer, 22edf20d, 5803, 2d3f, 92, 47, b4, 84, 2e, 5e, 93, 22) : IUnknown
{
    virtual int RegisterTick (
        IAvnActionCallback* callback
    ) = 0;
    virtual void Start () = 0;
    virtual void Stop () = 0;
    virtual bool RunsInBackground () = 0;
};
