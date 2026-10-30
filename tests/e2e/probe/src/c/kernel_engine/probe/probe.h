#ifndef KERNEL_ENGINE_PROBE_PROBE_H_
#define KERNEL_ENGINE_PROBE_PROBE_H_

#include <stdint.h>

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

/// [value]
typedef struct ke_probe_item
{
    uint32_t id;
    float weight; ///< [default:1]
    ke_probe_kind kind;
} ke_probe_item;

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

#endif
