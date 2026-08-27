#pragma once

#include <QObject>
#include <QPoint>
#include <QPointer>

class QEvent;
class QWidget;

namespace QtWaylandWindowKit {

class DraggableWidgetController final : public QObject
{
public:
    explicit DraggableWidgetController(QWidget *target, QObject *parent = nullptr);

    void addHandle(QWidget *handle, bool acceptRawTouch = false);
    void setEnabled(bool enabled);
    bool isEnabled() const;

protected:
    bool eventFilter(QObject *watched, QEvent *event) override;

private:
    void beginDrag(const QPoint &globalPosition);
    void updateDrag(const QPoint &globalPosition);
    void endDrag();
    QPoint boundedPosition(const QPoint &candidate) const;

    QPointer<QWidget> m_target;
    QPoint m_pressGlobalPosition;
    QPoint m_targetStartPosition;
    QPoint m_lastGlobalPosition;
    bool m_enabled = true;
    bool m_dragging = false;
};

} // namespace QtWaylandWindowKit
