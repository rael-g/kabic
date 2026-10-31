#ifndef KERNEL_ENGINE_PROBE_PROBE_H_
#define KERNEL_ENGINE_PROBE_PROBE_H_

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
    float weight; ///< [default:1]
    ke_probe_kind kind;
} ke_probe_item;

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
