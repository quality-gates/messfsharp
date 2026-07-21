module ShadowedBindings

let private outer value =
    let inner value = value
    inner 1
