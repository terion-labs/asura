// Exercise the real Objective-C bridge, including the borrowed SetNode contract.
// A managed peer finalizer is represented by TestPeer's destructor.
#define COM_GUIDS_MATERIALIZE
#include "comimpl.h"
#include "avalonia-native.h"
#import "automation.h"
#include <cstdio>
#include <cstdlib>

@interface AvnAccessibilityElement (LifetimeTest)
- (void)recalculateChildren;
@end

static void require(bool condition, const char* message)
{
    if (!condition) { std::fprintf(stderr, "%s\n", message); std::exit(1); }
}

template<class T>
static unsigned references(const ComPtr<T>& value)
{
    auto count = value->AddRef();
    value->Release();
    return count - 1;
}

class TestArray : public ComSingleObject<IAvnAutomationPeerArray, &IID_IAvnAutomationPeerArray>
{
    ComPtr<IAvnAutomationPeer> child;
public:
    FORWARD_IUNKNOWN()
    static int live;
    explicit TestArray(IAvnAutomationPeer* value) : child(value) { ++live; }
    ~TestArray() override { --live; }
    unsigned int GetCount() override { return child ? 1 : 0; }
    HRESULT Get(unsigned int index, IAvnAutomationPeer** value) override
    {
        if (index != 0 || !child) return E_INVALIDARG;
        *value = child.getRetainedReference();
        return S_OK;
    }
};
int TestArray::live = 0;

class TestPeer : public ComSingleObject<IAvnAutomationPeer, &IID_IAvnAutomationPeer>
{
    // Avalonia's SetNode callback stores a borrowed proxy, not an owning one.
    IAvnAutomationNode* node = nullptr;
public:
    FORWARD_IUNKNOWN()
    static int live;
    ComPtr<IAvnAutomationPeer> child;
    TestPeer() { ++live; }
    ~TestPeer() override { if (node) node->Dispose(); --live; }
    IAvnAutomationNode* GetNode() override { if (node) node->AddRef(); return node; }
    void SetNode(IAvnAutomationNode* value) override
    {
        require(node == nullptr, "SetNode called twice for the same managed peer");
        node = value;
    }
    IAvnAutomationPeerArray* GetChildren() override { return new TestArray(child); }
    IAvnAutomationPeer* GetParent() override { return child.getRetainedReference(); }
    IAvnAutomationPeer* GetRootPeer() override { return child.getRetainedReference(); }
    IAvnAutomationPeer* GetVisualRoot() override { return child.getRetainedReference(); }
    IAvnAutomationPeer* ScrollProvider_GetHorizontalScrollBar() override { return child.getRetainedReference(); }
    IAvnAutomationPeer* ScrollProvider_GetVerticalScrollBar() override { return child.getRetainedReference(); }
    IAvnString* GetAcceleratorKey() override { return {}; }
    IAvnString* GetAccessKey() override { return {}; }
    AvnAutomationControlType GetAutomationControlType() override { return {}; }
    IAvnString* GetAutomationId() override { return {}; }
    AvnRect GetBoundingRectangle() override { return {}; }
    IAvnString* GetClassName() override { return {}; }
    IAvnAutomationPeer* GetLabeledBy() override { return {}; }
    IAvnString* GetName() override { return {}; }
    IAvnAutomationPeer* GetTemplatedParent() override { return {}; }
    bool HasKeyboardFocus() override { return {}; }
    bool IsContentElement() override { return {}; }
    bool IsControlElement() override { return {}; }
    bool IsEnabled() override { return {}; }
    bool IsKeyboardFocusable() override { return {}; }
    void SetFocus() override {  }
    bool ShowContextMenu() override { return {}; }
    void BringIntoView() override {  }
    bool IsInteropPeer() override { return {}; }
    void* InteropPeer_GetNativeControlHandle() override { return {}; }
    bool IsRootProvider() override { return {}; }
    IAvnWindowBase* RootProvider_GetWindow() override { return {}; }
    IAvnAutomationPeer* RootProvider_GetFocus() override { return {}; }
    IAvnAutomationPeer* RootProvider_GetPeerFromPoint(AvnPoint point) override { (void)point; return {}; }
    bool IsEmbeddedRootProvider() override { return {}; }
    IAvnAutomationPeer* EmbeddedRootProvider_GetFocus() override { return {}; }
    IAvnAutomationPeer* EmbeddedRootProvider_GetPeerFromPoint(AvnPoint point) override { (void)point; return {}; }
    bool IsExpandCollapseProvider() override { return {}; }
    bool ExpandCollapseProvider_GetIsExpanded() override { return {}; }
    bool ExpandCollapseProvider_GetShowsMenu() override { return {}; }
    void ExpandCollapseProvider_Expand() override {  }
    void ExpandCollapseProvider_Collapse() override {  }
    bool IsInvokeProvider() override { return {}; }
    void InvokeProvider_Invoke() override {  }
    bool IsRangeValueProvider() override { return {}; }
    double RangeValueProvider_GetValue() override { return {}; }
    double RangeValueProvider_GetMinimum() override { return {}; }
    double RangeValueProvider_GetMaximum() override { return {}; }
    double RangeValueProvider_GetSmallChange() override { return {}; }
    double RangeValueProvider_GetLargeChange() override { return {}; }
    void RangeValueProvider_SetValue(double value) override { (void)value;  }
    bool RangeValueProvider_IsReadOnly() override { return {}; }
    bool IsSelectionItemProvider() override { return {}; }
    bool SelectionItemProvider_IsSelected() override { return {}; }
    void SelectionItemProvider_Select() override {  }
    void SelectionItemProvider_AddToSelection() override {  }
    void SelectionItemProvider_RemoveFromSelection() override {  }
    bool IsToggleProvider() override { return {}; }
    int ToggleProvider_GetToggleState() override { return {}; }
    void ToggleProvider_Toggle() override {  }
    bool IsValueProvider() override { return {}; }
    IAvnString* ValueProvider_GetValue() override { return {}; }
    void ValueProvider_SetValue(const char* value) override { (void)value;  }
    bool ValueProvider_IsReadOnly() override { return {}; }
    IAvnString* GetHelpText() override { return {}; }
    IAvnString* GetPlaceholderText() override { return {}; }
    AvnLandmarkType GetLandmarkType() override { return {}; }
    int GetHeadingLevel() override { return {}; }
    AvnLiveSetting GetLiveSetting() override { return {}; }
};
int TestPeer::live = 0;

int main()
{
    @autoreleasepool
    {
        auto peer = comnew<TestPeer>();
        peer->child.setNoAddRef(new TestPeer());
        __weak id retired = nil;
        for (int incarnation = 0; incarnation < 20; ++incarnation)
        {
            @autoreleasepool
            {
                AvnAccessibilityElement* element = [AvnAccessibilityElement acquire:peer];
                require(element != nil, "Cannot recreate a released accessibility element");
                retired = element;
                for (int refresh = 0; refresh < 100; ++refresh)
                {
                    @autoreleasepool
                    {
                        [element recalculateChildren];
                        require([[element accessibilityChildren] count] == 1, "Missing accessible child");
                        require([AvnAccessibilityElement acquire:peer] == element, "Element identity changed");
                        (void)[element accessibilityParent];
                        (void)[element accessibilityTopLevelUIElement];
                        (void)[element accessibilityWindow];
                        (void)[element accessibilityHorizontalScrollBar];
                        (void)[element accessibilityVerticalScrollBar];
                    }
                    require(TestArray::live == 0, "Accessibility refresh leaked a children array");
                    require(references(peer) == 2, "Accessibility refresh leaked a peer reference");
                    require(references(peer->child) == 2, "Accessibility query leaked a child reference");
                    ComPtr<IAvnAutomationNode> node(peer->GetNode(), true);
                    require(references(node) == 2, "Accessibility query leaked a node reference");
                }
                element = nil;
            }
            require(retired == nil, "Notification node retained a removed accessibility element");
            require(references(peer) == 1, "Removed element retained its managed peer");
            require(references(peer->child) == 1, "Removed element retained its child peer");
        }
    }
    require(TestPeer::live == 0 && TestArray::live == 0, "Peers survived their finalization");
    std::puts("PASS macOS accessibility: 2,000 refreshes, balanced COM references, removed elements released and recreated");
}
