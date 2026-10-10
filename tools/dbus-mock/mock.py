import dbus, dbus.service, dbus.lowlevel
from dbus.mainloop.glib import DBusGMainLoop
from gi.repository import GLib
DBusGMainLoop(set_as_default=True)
bus = dbus.SessionBus()
def log(*a): print("MOCK", *a, flush=True)

def emit(path, iface, member, sig, *args):
    m = dbus.lowlevel.SignalMessage(path, iface, member)
    m.append(*args, signature=sig)
    bus.send_message(m)

class Accel(dbus.service.Object):
    @dbus.service.method("org.kde.KGlobalAccel", in_signature="as")
    def doRegister(self, aid): log("doRegister", list(map(str, aid)))
    @dbus.service.method("org.kde.KGlobalAccel", in_signature="asaiu", out_signature="ai")
    def setShortcut(self, aid, keys, flags):
        log("setShortcut", list(map(str, aid)), [hex(k) for k in keys], int(flags)); return keys
    @dbus.service.method("org.kde.KGlobalAccel", in_signature="s", out_signature="o")
    def getComponent(self, name):
        log("getComponent", str(name))
        GLib.timeout_add(300, lambda: emit("/component/clicky", "org.kde.kglobalaccel.Component",
            "globalShortcutPressed", "ssx", "clicky", "show-grid", dbus.Int64(1)) and False)
        return dbus.ObjectPath("/component/clicky")

def respond(sender, opts, results):
    path = "/org/freedesktop/portal/desktop/request/%s/%s" % (sender[1:].replace(".", "_"), opts["handle_token"])
    GLib.timeout_add(50, lambda: emit(path, "org.freedesktop.portal.Request", "Response", "ua{sv}", dbus.UInt32(0), results) and False)
    return dbus.ObjectPath(path)

class Portal(dbus.service.Object):
    @dbus.service.method("org.freedesktop.portal.RemoteDesktop", in_signature="a{sv}", out_signature="o", sender_keyword="s")
    def CreateSession(self, opts, s):
        log("CreateSession", dict(opts)); return respond(s, opts, {"session_handle": "/org/freedesktop/portal/desktop/session/x/" + str(opts["session_handle_token"])})
    @dbus.service.method("org.freedesktop.portal.RemoteDesktop", in_signature="oa{sv}", out_signature="o", sender_keyword="s")
    def SelectDevices(self, session, opts, s):
        log("SelectDevices", str(session), dict(opts)); return respond(s, opts, {})
    @dbus.service.method("org.freedesktop.portal.ScreenCast", in_signature="oa{sv}", out_signature="o", sender_keyword="s")
    def SelectSources(self, session, opts, s):
        log("SelectSources", dict(opts)); return respond(s, opts, {})
    @dbus.service.method("org.freedesktop.portal.RemoteDesktop", in_signature="osa{sv}", out_signature="o", sender_keyword="s")
    def Start(self, session, parent, opts, s):
        log("Start", repr(str(parent)))
        streams = dbus.Array([
            dbus.Struct((dbus.UInt32(71), {"position": dbus.Struct((dbus.Int32(1920), dbus.Int32(0))), "size": dbus.Struct((dbus.Int32(1920), dbus.Int32(1080)))}), signature="ua{sv}"),
            dbus.Struct((dbus.UInt32(70), {"position": dbus.Struct((dbus.Int32(0), dbus.Int32(0))), "size": dbus.Struct((dbus.Int32(1920), dbus.Int32(1080)))}), signature="ua{sv}"),
        ], signature="(ua{sv})")
        return respond(s, opts, {"devices": dbus.UInt32(2), "streams": streams, "restore_token": "tok-123"})
    @dbus.service.method("org.freedesktop.portal.RemoteDesktop", in_signature="oa{sv}udd")
    def NotifyPointerMotionAbsolute(self, session, opts, stream, x, y): log("Motion", int(stream), float(x), float(y))
    @dbus.service.method("org.freedesktop.portal.RemoteDesktop", in_signature="oa{sv}iu")
    def NotifyPointerButton(self, session, opts, button, state): log("Button", hex(button), int(state))

Accel(dbus.service.BusName("org.kde.kglobalaccel", bus), "/kglobalaccel")
Portal(dbus.service.BusName("org.freedesktop.portal.Desktop", bus), "/org/freedesktop/portal/desktop")
log("ready")
GLib.MainLoop().run()
