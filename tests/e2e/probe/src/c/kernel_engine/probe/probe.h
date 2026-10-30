#ifndef KERNEL_ENGINE_PROBE_PROBE_H_
#define KERNEL_ENGINE_PROBE_PROBE_H_

#include <stdint.h>

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
    void (*fill)(struct ke_probe *self, const ke_probe_item *items, uint32_t item_count);
} ke_probe;

typedef struct ke_probe_handle
{
    ke_probe *ref;
    void (*destroy)(ke_probe *self);
} ke_probe_handle;

#endif
