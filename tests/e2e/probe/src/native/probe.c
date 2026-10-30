#include <kernel_engine/probe/probe.h>
#include <stdlib.h>

static ke_probe_item stored[4];
static uint32_t stored_count;
static ke_probe_batch current;
static void *bound;

static uint32_t count(ke_probe *self) { (void)self; return stored_count; }
static float weight_of(ke_probe *self, uint32_t id) { (void)self; return stored[id].weight; }
static void fill(ke_probe *self, const ke_probe_item *items, uint32_t item_count)
{
    (void)self;
    stored_count = item_count;
    for (uint32_t i = 0; i < item_count; i++) stored[i] = items[i];
}
static ke_result read_value(ke_probe *self, uint32_t id, float *out_value)
{
    (void)self;
    if (id >= stored_count) return KE_RESULT_GENERAL;
    *out_value = stored[id].weight;
    return KE_OK;
}
static ke_result each(ke_probe *self, ke_probe_visit_fn visit, void *ctx)
{
    (void)self;
    for (uint32_t i = 0; i < stored_count; i++) visit(ctx, stored[i].id);
    return KE_OK;
}
static const ke_probe_batch *batch(ke_probe *self)
{
    (void)self;
    current.items = stored;
    current.item_count = stored_count;
    return &current;
}
static void tag(ke_probe *self, const ke_probe_label *label) { (void)self; (void)label; }
static void load(ke_probe *self, uint32_t id, ke_probe_done_fn on_done, void *ctx)
{
    (void)self;
    on_done(ctx, KE_OK, id + 1);
}
static ke_result bind(ke_probe *self, uint32_t id, void *instance) { (void)self; (void)id; bound = instance; return KE_OK; }
static void unbind(ke_probe *self, uint32_t id) { (void)self; (void)id; bound = NULL; }
static void destroy(ke_probe *self) { free(self); }

__attribute__((visibility("default"))) ke_result ke_probe_create(uint32_t capacity, ke_probe_handle *out_handle)
{
    (void)capacity;
    ke_probe *p = calloc(1, sizeof(ke_probe));
    if (!p) return KE_RESULT_GENERAL;
    p->count = count;
    p->weight_of = weight_of;
    p->fill = fill;
    p->read = read_value;
    p->each = each;
    p->batch = batch;
    p->tag = tag;
    p->load = load;
    p->bind = bind;
    p->unbind = unbind;
    out_handle->ref = p;
    out_handle->destroy = destroy;
    return KE_OK;
}
