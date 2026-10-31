#ifndef PROBE_EXTRA_H_
#define PROBE_EXTRA_H_

#include <kernel_engine/probe/probe.h>

/// Makes a probe another way.
/// @param capacity Items.
/// @param out_handle [out] The probe.
ke_result ke_probe_extra_create(uint32_t capacity, ke_probe_handle *out_handle);

#endif
