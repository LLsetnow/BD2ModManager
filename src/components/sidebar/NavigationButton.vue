<script setup lang="ts">
import { computed } from 'vue';
import { RouterLink } from 'vue-router';
import router from '../../router';

const props = defineProps<{
    icon: any
    label: string
    value?: string
    disabled?: boolean
}>()

const emit = defineEmits<{ click: [] }>()

const active = computed(() => {
    return !!props.value && router.currentRoute.value.name === props.value
})
</script>
<template>
    <component :is="value ? RouterLink : 'button'"
        v-bind="value ? { to: value } : { type: 'button', disabled }"
        @click="!value && emit('click')"
        :class="{ 
            'bg-accent! text-text-on-accent!': active, 
            'text-text-secondary': !active 
        }" 
        class="
        w-full px-3 py-2 gap-3 rounded-full 
        cursor-pointer capitalize
        transition-colors duration-200 transform active:scale-98
        flex items-center 
        text-sm font-medium text-left disabled:opacity-50 disabled:cursor-not-allowed
        hover:bg-state-hover hover:text-text-primary">
        <component :is="icon" class="w-[1.75em] h-[1.75em] text-text-primary transition-colors duration-200" :class="{'text-text-on-accent!': active}" />
        {{ label }}
        
    </component>
</template>
<style scoped></style>
