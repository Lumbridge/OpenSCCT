/* The stock layout uses the first active viewport. A normalized dimension
   scales with its parent when requested, or with the viewport otherwise. */
struct gui_component;
typedef float (__fastcall *dimension_query)(struct gui_component *);
struct gui_methods {
    void *earlier[26];
    dimension_query width, height, left, top;
};
struct display_viewport {
    unsigned char prefix[0xa0];
    int width, height;
};
struct gui_display {
    unsigned char prefix[0x2c];
    const struct display_viewport **viewports;
    int viewport_count;
};
struct gui_session {
    unsigned char prefix[0x28];
    const struct gui_display *display;
};
struct gui_context {
    unsigned char prefix[0x30];
    const struct gui_session *session;
};
struct gui_component {
    const struct gui_methods *methods;
    unsigned char before_context[0x24];
    const struct gui_context *context;
    struct gui_component *parent;
    unsigned char before_flags[0x2c];
    unsigned int bound_to_parent : 1;
    unsigned int scale_to_parent : 1;
    unsigned char before_placement[0x10];
    float top, left, width, height;
};

/* Link addresses of the pooled 0.0f and 1.0f constants in Steam build 252084. */
#define no_dimension (*(const float *)0x10c109f8u)
#define normalized_limit (*(const float *)0x10c109fcu)

float __fastcall gui_actual_width(struct gui_component *self)
{
    const struct gui_context *context = self->context;
    const struct display_viewport *view;
    if (!context || !context->session || !context->session->display ||
        !context->session->display->viewport_count || !(view = context->session->display->viewports[0]))
        return no_dimension;
    if (self->width <= normalized_limit) {
        struct gui_component *owner;
        if (self->scale_to_parent && (owner = self->parent))
            return self->width * owner->methods->width(owner);
        return self->width * view->width;
    }
    return self->width;
}

float __fastcall gui_actual_height(struct gui_component *self)
{
    const struct gui_context *context = self->context;
    const struct display_viewport *view;
    if (!context || !context->session || !context->session->display ||
        !context->session->display->viewport_count || !(view = context->session->display->viewports[0]))
        return no_dimension;
    if (self->height <= normalized_limit) {
        struct gui_component *owner;
        if (self->scale_to_parent && (owner = self->parent))
            return self->height * owner->methods->height(owner);
        return self->height * view->height;
    }
    return self->height;
}
