/* Minimal window manager for headless screenshots: grants map requests and geometry so a GTK
   toplevel actually maps and paints under Xvfb (a bare X server maps nothing on its own). Just enough
   to photograph the window — no decorations, focus or input handling. Public-domain, tinywm-style. */
#include <X11/Xlib.h>
#include <stdio.h>

static int OnError(Display *display, XErrorEvent *error) {
  fprintf(stderr, "screenshot-wm: X error code %d, request %d\n", error->error_code, error->request_code);
  fflush(stderr);
  return 0;
}

int main(void) {
  Display *display = XOpenDisplay(0);
  if (!display) {
    fprintf(stderr, "screenshot-wm: cannot open display\n");
    return 1;
  }

  Window root = DefaultRootWindow(display);
  XSetErrorHandler(OnError);
  XSelectInput(display, root, SubstructureRedirectMask | SubstructureNotifyMask);
  XSync(display, False);

  for (;;) {
    XEvent event;
    XNextEvent(display, &event);
    if (event.type == MapRequest) {
      XMapWindow(display, event.xmaprequest.window);
      XMoveWindow(display, event.xmaprequest.window, 0, 0);
      XRaiseWindow(display, event.xmaprequest.window);
    } else if (event.type == ConfigureRequest) {
      XConfigureRequestEvent *c = &event.xconfigurerequest;
      XWindowChanges changes;
      changes.x = 0;
      changes.y = 0;
      changes.width = c->width;
      changes.height = c->height;
      changes.border_width = 0;
      changes.sibling = c->above;
      changes.stack_mode = c->detail;
      XConfigureWindow(display, c->window, c->value_mask, &changes);
    }
  }
}
