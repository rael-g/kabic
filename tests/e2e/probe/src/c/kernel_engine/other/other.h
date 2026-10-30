#ifndef KERNEL_ENGINE_OTHER_OTHER_H_
#define KERNEL_ENGINE_OTHER_OTHER_H_

#include <kernel_engine/probe/probe.h>

/// [value]
typedef struct ke_other_pair
{
    ke_probe_item first;
    ke_probe_kind kind;
} ke_other_pair;

typedef struct ke_other
{
    void *handle;
    ke_probe_kind (*kind_of)(struct ke_other *self, const ke_probe_item *item);
    ke_other_pair (*pair)(struct ke_other *self);
} ke_other;

/// [owner]
typedef struct ke_other_handle
{
    ke_other *ref;
    void (*destroy)(ke_other *self);
} ke_other_handle;

#endif
