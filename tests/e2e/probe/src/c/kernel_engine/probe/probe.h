#ifndef KERNEL_ENGINE_PROBE_PROBE_H_
#define KERNEL_ENGINE_PROBE_PROBE_H_

#include <stdbool.h>
#include <stdint.h>

#ifdef __cplusplus
extern "C" {
#endif

typedef enum ke_result
{
    KE_OK,
    KE_RESULT_GENERAL,
} ke_result;

/**
 * @param ctx [context] Opaque.
 * @param id Item.
 */
typedef void (*ke_probe_visit_fn)(void *ctx, uint32_t id);

typedef enum ke_probe_kind
{
    KE_PROBE_KIND_NEAR,
    KE_PROBE_KIND_FAR,
} ke_probe_kind;

/// [id]
typedef uint32_t ke_probe_id;

typedef struct ke_probe_item
{
    ke_probe_id id;
    float weight;
    ke_probe_kind kind;
} ke_probe_item;

/** [defaults] */
static const ke_probe_item ke_probe_item_defaults = {
    .weight = -1.5f,
};

/** The item a probe starts from. */
static const ke_probe_item ke_probe_item_start = {
    .id = _Generic((ke_probe_id)0, uint32_t: 7u, default: 0u),
    .weight = 1.0f,
    .kind = KE_PROBE_KIND_FAR,
};

#define KE_PROBE_SIZE_OF(T, m) (uint32_t)sizeof(((T *)0)->m)

#define KE_PROBE_PLACE(T, m, extra) { (uint32_t)__builtin_offsetof(T, m), (extra) }

typedef struct ke_probe_options
{
    const char *name; ///< [utf8]
    bool loud;
} ke_probe_options;

/** [defaults] */
static const ke_probe_options ke_probe_options_defaults = {
    .name = "probe",
    .loud = true,
};

typedef struct ke_probe_place
{
    uint32_t offset;
    uint32_t extra;
} ke_probe_place;

/** Where an item's kind lives, measured through a macro taking more than the member. */
static const ke_probe_place ke_probe_kind_place = KE_PROBE_PLACE(ke_probe_item, kind, 3);

/** The width of an item's weight, measured through a macro. */
static const uint32_t ke_probe_weight_size = KE_PROBE_SIZE_OF(ke_probe_item, weight);

/// [view] Items a call hands out.
typedef struct ke_probe_batch
{
    const ke_probe_item *items; ///< [array_of:item_count]
    uint32_t item_count;
} ke_probe_batch;

/// [borrowed] A name the call reads.
typedef struct ke_probe_label
{
    const uint32_t *ids; ///< [array_of:id_count]
    uint32_t id_count;
} ke_probe_label;

/**
 * @param ctx [context] Opaque.
 * @param code Outcome.
 * @param value The result.
 */
typedef void (*ke_probe_done_fn)(void *ctx, ke_result code, uint32_t value);

typedef struct ke_probe
{
    void *handle;
    uint32_t (*count)(struct ke_probe *self);
    float (*weight_of)(struct ke_probe *self, uint32_t id);
    /**
     * @param items [array_of:item_count] Items.
     * @param item_count Count.
     */
    void (*fill)(struct ke_probe *self, const ke_probe_item *items, uint32_t item_count);
    /** @param out_value [out] Value. */
    ke_result (*read)(struct ke_probe *self, uint32_t id, float *out_value);
    /**
     * @param visit [closure:ctx] Called per item.
     * @param ctx [context] Opaque.
     */
    ke_result (*each)(struct ke_probe *self, ke_probe_visit_fn visit, void *ctx);
    /** @return [view] The current batch. */
    const ke_probe_batch *(*batch)(struct ke_probe *self);
    /** @param label [borrowed] Read during the call. */
    void (*tag)(struct ke_probe *self, const ke_probe_label *label);
    /**
     * @param on_done [completion:ctx] Called once.
     * @param ctx [context] Opaque.
     */
    void (*load)(struct ke_probe *self, uint32_t id, ke_probe_done_fn on_done, void *ctx);
    /** @param item [optional] Null counts every item. */
    uint32_t (*count_of)(struct ke_probe *self, const ke_probe_item *item);
    /** @param instance [rooted:id] Kept until unbind. */
    ke_result (*bind)(struct ke_probe *self, uint32_t id, void *instance);
    /** [unroots:id] Drops the binding. */
    void (*unbind)(struct ke_probe *self, uint32_t id);
} ke_probe;

/// [owner]
typedef struct ke_probe_handle
{
    ke_probe *ref;
    void (*destroy)(ke_probe *self);
} ke_probe_handle;

/// Makes a probe.
/// @param capacity Items.
/// @param out_handle [out] The probe.
ke_result ke_probe_create(uint32_t capacity, ke_probe_handle *out_handle);

#ifdef __cplusplus
}
#endif

#endif
