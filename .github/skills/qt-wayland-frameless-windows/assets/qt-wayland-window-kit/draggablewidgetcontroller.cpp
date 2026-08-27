#include "draggablewidgetcontroller.h"

#include <QEvent>
#include <QMouseEvent>
#include <QTouchEvent>
#include <QWidget>

namespace QtWaylandWindowKit {

namespace {

QPoint mouseGlobalPosition(const QMouseEvent *event)
{
#if QT_VERSION >= QT_VERSION_CHECK(6, 0, 0)
    return event->globalPosition().toPoint();
#else
    return event->globalPos();
#endif
}

bool touchGlobalPosition(const QTouchEvent *event, QPoint *position)
{
#if QT_VERSION >= QT_VERSION_CHECK(6, 0, 0)
    if (event->points().isEmpty()) {
        return false;
    }
    *position = event->points().first().globalPosition().toPoint();
#else
    if (event->touchPoints().isEmpty()) {
        return false;
    }
    *position = event->touchPoints().first().screenPos().toPoint();
#endif
    return true;
}

} // namespace

DraggableWidgetController::DraggableWidgetController(QWidget *target, QObject *parent)
    : QObject(parent ? parent : target)
    , m_target(target)
{
}

void DraggableWidgetController::addHandle(QWidget *handle, bool acceptRawTouch)
{
    if (!handle) {
        return;
    }

    if (acceptRawTouch) {
        handle->setAttribute(Qt::WA_AcceptTouchEvents, true);
    }
    handle->installEventFilter(this);
}

void DraggableWidgetController::setEnabled(bool enabled)
{
    m_enabled = enabled;
    if (!m_enabled) {
        endDrag();
    }
}

bool DraggableWidgetController::isEnabled() const
{
    return m_enabled;
}

bool DraggableWidgetController::eventFilter(QObject *watched, QEvent *event)
{
    Q_UNUSED(watched);

    if (!m_target) {
        return QObject::eventFilter(watched, event);
    }

    switch (event->type()) {
    case QEvent::MouseButtonPress: {
        auto *mouseEvent = static_cast<QMouseEvent *>(event);
        if (!m_enabled || mouseEvent->button() != Qt::LeftButton) {
            break;
        }
        beginDrag(mouseGlobalPosition(mouseEvent));
        event->accept();
        return true;
    }
    case QEvent::MouseMove: {
        if (!m_dragging) {
            break;
        }
        updateDrag(mouseGlobalPosition(static_cast<QMouseEvent *>(event)));
        event->accept();
        return true;
    }
    case QEvent::MouseButtonRelease: {
        auto *mouseEvent = static_cast<QMouseEvent *>(event);
        if (mouseEvent->button() != Qt::LeftButton || !m_dragging) {
            break;
        }
        m_lastGlobalPosition = mouseGlobalPosition(mouseEvent);
        endDrag();
        event->accept();
        return true;
    }
    case QEvent::TouchBegin:
    case QEvent::TouchUpdate:
    case QEvent::TouchEnd:
    case QEvent::TouchCancel: {
        if (!m_enabled && event->type() == QEvent::TouchBegin) {
            break;
        }

        QPoint globalPosition = m_lastGlobalPosition;
        touchGlobalPosition(static_cast<QTouchEvent *>(event), &globalPosition);

        if (event->type() == QEvent::TouchBegin) {
            beginDrag(globalPosition);
        } else if (event->type() == QEvent::TouchUpdate && m_dragging) {
            updateDrag(globalPosition);
        } else if ((event->type() == QEvent::TouchEnd
                    || event->type() == QEvent::TouchCancel)
                   && m_dragging) {
            m_lastGlobalPosition = globalPosition;
            endDrag();
        }

        event->accept();
        return true;
    }
    case QEvent::Hide:
    case QEvent::Destroy:
        endDrag();
        break;
    default:
        break;
    }

    return QObject::eventFilter(watched, event);
}

void DraggableWidgetController::beginDrag(const QPoint &globalPosition)
{
    if (!m_enabled || !m_target) {
        return;
    }

    m_dragging = true;
    m_pressGlobalPosition = globalPosition;
    m_lastGlobalPosition = globalPosition;
    m_targetStartPosition = m_target->pos();
}

void DraggableWidgetController::updateDrag(const QPoint &globalPosition)
{
    if (!m_enabled || !m_dragging || !m_target) {
        return;
    }

    m_lastGlobalPosition = globalPosition;
    const QPoint candidate = m_targetStartPosition
            + globalPosition
            - m_pressGlobalPosition;
    m_target->move(boundedPosition(candidate));
}

void DraggableWidgetController::endDrag()
{
    m_dragging = false;
}

QPoint DraggableWidgetController::boundedPosition(const QPoint &candidate) const
{
    if (!m_target || m_target->isWindow()) {
        return candidate;
    }

    QWidget *host = m_target->parentWidget();
    if (!host) {
        return candidate;
    }

    const int maximumX = qMax(0, host->width() - m_target->width());
    const int maximumY = qMax(0, host->height() - m_target->height());
    return QPoint(qBound(0, candidate.x(), maximumX),
                  qBound(0, candidate.y(), maximumY));
}

} // namespace QtWaylandWindowKit
