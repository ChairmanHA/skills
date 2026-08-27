#include "waylandwindowcontroller.h"

#include "draggablewidgetcontroller.h"

#include <QAbstractButton>
#include <QDialog>
#include <QEvent>
#include <QGuiApplication>
#include <QTimer>
#include <QWidget>

namespace QtWaylandWindowKit {

WaylandWindowController::WaylandWindowController(QWidget *window,
                                                 QWidget *applicationHost,
                                                 Mode mode,
                                                 QObject *parent)
    : QObject(parent ? parent : window)
    , m_window(window)
    , m_host(applicationHost)
    , m_mode(mode)
{
    m_dragController = new DraggableWidgetController(m_window, this);

    if (!m_window || !m_host || !isWaylandPlatform()) {
        return;
    }

    m_hosted = true;
    applyHostedPresentation();
    m_window->installEventFilter(this);
    m_host->installEventFilter(this);
}

WaylandWindowController::~WaylandWindowController()
{
    if (m_host) {
        m_host->removeEventFilter(this);
    }
    hideModalOverlay();
}

bool WaylandWindowController::isHosted() const
{
    return m_hosted;
}

WaylandWindowController::Mode WaylandWindowController::mode() const
{
    return m_mode;
}

void WaylandWindowController::addDragHandle(QWidget *handle, bool acceptRawTouch)
{
    if (m_dragController) {
        m_dragController->addHandle(handle, acceptRawTouch);
    }
}

void WaylandWindowController::setDragEnabled(bool enabled)
{
    if (m_dragController) {
        m_dragController->setEnabled(enabled);
    }
}

void WaylandWindowController::bindCloseButton(QAbstractButton *button)
{
    if (!button) {
        return;
    }

    connect(button, &QAbstractButton::clicked, this, [this]() {
        if (!m_window) {
            return;
        }
        if (auto *dialog = qobject_cast<QDialog *>(m_window.data())) {
            dialog->reject();
        } else {
            m_window->close();
        }
    });
}

void WaylandWindowController::present()
{
    if (!m_window) {
        return;
    }

    if (!m_hosted) {
        if (m_mode == Mode::ModalDialog) {
            if (auto *dialog = qobject_cast<QDialog *>(m_window.data())) {
                dialog->open();
                return;
            }
        }
        m_window->show();
        return;
    }

    m_window->adjustSize();
    if (!m_positionInitialized) {
        centerInHost();
    } else {
        clampWindowToHost();
    }

    if (m_mode == Mode::ModalDialog) {
        showModalOverlay();
    }

    m_window->show();
    m_window->raise();
}

void WaylandWindowController::centerInHost()
{
    if (!m_hosted || !m_window || !m_host) {
        return;
    }

    const int maximumX = qMax(0, m_host->width() - m_window->width());
    const int maximumY = qMax(0, m_host->height() - m_window->height());
    const QPoint centered((m_host->width() - m_window->width()) / 2,
                          (m_host->height() - m_window->height()) / 2);
    m_window->move(qBound(0, centered.x(), maximumX),
                   qBound(0, centered.y(), maximumY));
    m_positionInitialized = true;
}

bool WaylandWindowController::eventFilter(QObject *watched, QEvent *event)
{
    if (watched == m_window.data()) {
        switch (event->type()) {
        case QEvent::Show:
            if (m_mode == Mode::ModalDialog) {
                showModalOverlay();
            }
            QTimer::singleShot(0, this, [this]() {
                if (!m_window) {
                    return;
                }
                if (!m_positionInitialized) {
                    centerInHost();
                } else {
                    clampWindowToHost();
                }
                m_window->raise();
            });
            break;
        case QEvent::Hide:
        case QEvent::Close:
        case QEvent::Destroy:
            hideModalOverlay();
            break;
        default:
            break;
        }
        return QObject::eventFilter(watched, event);
    }

    if (watched == m_host.data() && event->type() == QEvent::Resize) {
        updateModalOverlayGeometry();
        clampWindowToHost();
        if (m_modalOverlay) {
            m_modalOverlay->raise();
        }
        if (m_window && m_window->isVisible()) {
            m_window->raise();
        }
        return QObject::eventFilter(watched, event);
    }

    if (watched == m_modalOverlay.data()) {
        switch (event->type()) {
        case QEvent::MouseButtonPress:
        case QEvent::MouseButtonRelease:
        case QEvent::MouseMove:
        case QEvent::Wheel:
        case QEvent::TouchBegin:
        case QEvent::TouchUpdate:
        case QEvent::TouchEnd:
        case QEvent::TouchCancel:
            event->accept();
            return true;
        default:
            break;
        }
    }

    return QObject::eventFilter(watched, event);
}

bool WaylandWindowController::isWaylandPlatform()
{
#if defined(Q_OS_LINUX)
    return QGuiApplication::platformName().contains(
            QStringLiteral("wayland"), Qt::CaseInsensitive);
#else
    return false;
#endif
}

void WaylandWindowController::applyHostedPresentation()
{
    if (!m_window || !m_host) {
        return;
    }

    const bool wasHidden = !m_window->isVisible();
    m_window->setParent(m_host, Qt::Widget);
    m_window->setAttribute(Qt::WA_Moved, true);
    m_window->setWindowModality(Qt::NonModal);
    if (auto *dialog = qobject_cast<QDialog *>(m_window.data())) {
        dialog->setModal(false);
    }

    // A pre-created hosted child must not appear when its host is first shown.
    // Presentation remains the exclusive responsibility of present().
    if (wasHidden) {
        m_window->hide();
    }
}

void WaylandWindowController::showModalOverlay()
{
    if (!m_hosted || m_mode != Mode::ModalDialog || !m_window || !m_host) {
        return;
    }

    if (!m_modalOverlay) {
        m_modalOverlay = new QWidget(m_host);
        m_modalOverlay->setObjectName(QStringLiteral("waylandModalOverlay"));
        m_modalOverlay->setAttribute(Qt::WA_StyledBackground, true);
        m_modalOverlay->setAttribute(Qt::WA_AcceptTouchEvents, true);
        m_modalOverlay->setFocusPolicy(Qt::StrongFocus);
        m_modalOverlay->setStyleSheet(QStringLiteral(
                "QWidget#waylandModalOverlay { background: rgba(0, 0, 0, 0); }"));
        m_modalOverlay->installEventFilter(this);
    }

    updateModalOverlayGeometry();
    m_modalOverlay->show();
    m_modalOverlay->raise();
    m_window->raise();
}

void WaylandWindowController::hideModalOverlay()
{
    if (!m_modalOverlay) {
        return;
    }

    m_modalOverlay->hide();
    m_modalOverlay->deleteLater();
    m_modalOverlay = nullptr;
}

void WaylandWindowController::updateModalOverlayGeometry()
{
    if (m_modalOverlay && m_host) {
        m_modalOverlay->setGeometry(m_host->rect());
    }
}

void WaylandWindowController::clampWindowToHost()
{
    if (!m_hosted || !m_window || !m_host) {
        return;
    }

    const int maximumX = qMax(0, m_host->width() - m_window->width());
    const int maximumY = qMax(0, m_host->height() - m_window->height());
    const QPoint current = m_window->pos();
    m_window->move(qBound(0, current.x(), maximumX),
                   qBound(0, current.y(), maximumY));
}

} // namespace QtWaylandWindowKit
