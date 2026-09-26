<script setup lang="ts">
import { computed, ref } from 'vue'
import Modal from '../../../components/common/Modal.vue'
import Button from '../../../components/common/Button.vue'
import Input from '../../../components/common/Input.vue'

const visible = ref(false)
const sourceModName = ref('')
const sourceId = ref('')
const sourceType = ref<'Standing' | 'Cutscene'>('Standing')
const targetId = ref('')
const isSaving = ref(false)
const onSave = ref<((targetId: string) => Promise<void> | void) | undefined>()

defineExpose({
    open(payload: {
        modName: string
        sourceId: string
        modType: 'Standing' | 'Cutscene'
        onSave?: (targetId: string) => Promise<void> | void
    }) {
        sourceModName.value = payload.modName
        sourceId.value = payload.sourceId
        sourceType.value = payload.modType
        targetId.value = ''
        onSave.value = payload.onSave
        visible.value = true
    }
})

const isTargetIdValid = computed(() => /^\d{6}$/.test(targetId.value.trim()) && targetId.value.trim() !== sourceId.value)

async function createCopy() {
    if (!isTargetIdValid.value || isSaving.value) return
    isSaving.value = true
    try {
        await onSave.value?.(targetId.value.trim())
        visible.value = false
    } finally {
        isSaving.value = false
    }
}

function close() {
    if (isSaving.value) return
    visible.value = false
}
</script>

<template>
    <Modal v-model:show="visible" :title="$t('modsTab.modals.migrateModId.title')"
        :subtitle="$t('modsTab.modals.migrateModId.description', { modName: sourceModName })" @close="close">
        <div class="p-4 flex flex-col gap-4">
            <div class="rounded-md border border-border-default bg-surface-panel p-3 text-sm">
                <div class="flex justify-between gap-4">
                    <span class="text-text-secondary">{{ $t('modsTab.modals.migrateModId.resourceType') }}</span>
                    <span class="text-text-primary">{{ $t(`common.modTypes.${sourceType.toLowerCase()}`) }}</span>
                </div>
                <div class="mt-1 flex justify-between gap-4">
                    <span class="text-text-secondary">{{ $t('modsTab.modals.migrateModId.sourceId') }}</span>
                    <span class="font-mono text-text-primary">{{ sourceId }}</span>
                </div>
            </div>

            <div>
                <p class="mb-1 font-bold text-text-primary">{{ $t('modsTab.modals.migrateModId.targetId') }}</p>
                <Input v-model="targetId" inputmode="numeric" maxlength="6" autocomplete="off"
                    :placeholder="$t('modsTab.modals.migrateModId.targetIdPlaceholder')" />
                <p class="mt-2 text-xs text-text-secondary">
                    {{ $t('modsTab.modals.migrateModId.copyNote') }}
                </p>
                <p v-if="targetId && !isTargetIdValid" class="mt-1 text-xs text-error">
                    {{ $t('modsTab.modals.migrateModId.invalidTargetId') }}
                </p>
            </div>
        </div>

        <template #footer>
            <div class="flex justify-end gap-2 p-3">
                <Button variant="default" :disabled="isSaving" @click="close">
                    {{ $t('common.actions.cancel') }}
                </Button>
                <Button variant="primary" :disabled="!isTargetIdValid || isSaving" @click="createCopy">
                    {{ $t('modsTab.modals.migrateModId.createCopy') }}
                </Button>
            </div>
        </template>
    </Modal>
</template>
