#pragma once

#include <QObject>
#include <QPointer>

class QAbstractButton;
class QEvent;
class QWidget;

namespace QtWaylandWindowKit {

class DraggableWidgetController;

class WaylandWindowController final : public QObject
{
public:
    enum class Mode {
        ModalDialog,
        NonModalFloating
    };

    WaylandWindowController(QWidget *window,
                            QWidget *applicationHost,
                            Mode mode,
                            QObject *parent = nullptr);
    ~WaylandWindowController() override;

    bool isHosted() const;
    Mode mode() const;

    void addDragHandle(QWidget *handle, bool acceptRawTouch = false);
    void setDragEnabled(bool enabled);
    void bindCloseButton(QAbstractButton *button);

    void present();
    void centerInHost();

protected:
    bool eventFilter(QObject *watched, QEvent *event) override;

private:
    static bool isWaylandPlatform();

    void applyHostedPresentation();
    void showModalOverlay();
    void hideModalOverlay();
    void updateModalOverlayGeometry();
    void clampWindowToHost();

    QPointer<QWidget> m_window;
    QPointer<QWidget> m_host;
    QPointer<QWidget> m_modalOverlay;
    DraggableWidgetController *m_dragController = nullptr;
    Mode m_mode = Mode::NonModalFloating;
    bool m_hosted = false;
    bool m_positionInitialized = false;
};

} // namespace QtWaylandWindowKit
