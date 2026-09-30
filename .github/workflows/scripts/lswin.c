/* Print the viewable top-level windows (id and geometry), so the screenshot script can pick the
   GUI's window without a window-list tool installed. Public-domain. */
#include <X11/Xlib.h>
#include <stdio.h>

int main(void) {
  Display *display = XOpenDisplay(0);
  if (!display)
    return 1;

  Window root = DefaultRootWindow(display), returned_root, parent, *children = 0;
  unsigned int count = 0;
  if (!XQueryTree(display, root, &returned_root, &parent, &children, &count))
    return 1;

  for (unsigned int i = 0; i < count; ++i) {
    XWindowAttributes attributes;
    if (XGetWindowAttributes(display, children[i], &attributes)
        && attributes.map_state == IsViewable
        && attributes.width > 50 && attributes.height > 50)
      printf("0x%lx %dx%d+%d+%d\n", (unsigned long)children[i],
             attributes.width, attributes.height, attributes.x, attributes.y);
  }

  if (children)
    XFree(children);

  return 0;
}
